using System.Data;
using System.Globalization;
using System.Text;
using Dapper;
using Fias.Application.Abstractions;
using Fias.Application.Search;
using Npgsql;

namespace Fias.Infrastructure.Persistence;

/// <summary>
/// Гибридный поиск адресов на сыром SQL (Dapper) поверх ДЕНОРМАЛИЗОВАННОЙ проекции search.*.
///
/// Ранжирование — Reciprocal Rank Fusion (RRF) двух сигналов:
///   1) FTS  — кандидаты ранжируются по ts_rank(name_tsv, query);
///   2) pg_trgm — те же кандидаты ранжируются по similarity(name, query).
/// Итог = Σ 1/(k + rank_i) по тем спискам, где кандидат реально совпал. RRF не требует
/// сведения несравнимых шкал ts_rank и similarity к общим весам и устойчив к выбросам.
/// Плюс мягкий приоритет уровня (города 4/5 над одноимёнными сёлами) как небольшая добавка.
///
/// Поуровневое сужение (stage 2/3) идёт без единого JOIN:
///   • контейнер (регион/город) ищется в search.address_objects;
///   • улица — в его поддереве по денормализованному столбцу path (LIKE 'prefix.%');
///   • дом — по search.houses.parent_object_id = <улица> (индекс-сик), что и решает проблему
///     медленных JOIN при десятках млн домов. full_name и parent_guid уже лежат в строке.
/// </summary>
public sealed class AddressSearchRepository(NpgsqlDataSource dataSource) : IAddressSearchRepository
{
    // RRF: сглаживающая константа. k=60 — общепринятый дефолт (TREC). Чем больше, тем слабее
    // влияние верхних позиций.
    private const int RrfK = 60;

    // Мягкий приоритет крупных НП — добавка масштаба «около одной позиции RRF» (1/(k+1)≈0.016),
    // чтобы при прочих равных город обходил село, но не перебивал качество совпадения.
    private const double RrfLevelBoost = 0.0005;

    private const int HouseLevel = 10;

    // Выражение RRF-скора поверх CTE `ranked` (r_fts/r_trgm — позиции в списках; fts_match/
    // trgm_match — реальное совпадение в соответствующем списке). Общее для поиска и ResolveBest.
    private const string RrfScoreExpr = """
        ( CASE WHEN fts_match  THEN 1.0 / (@rrfK + r_fts)  ELSE 0 END
          + CASE WHEN trgm_match THEN 1.0 / (@rrfK + r_trgm) ELSE 0 END
          + CASE level WHEN 4 THEN @levelBoost WHEN 5 THEN @levelBoost WHEN 6 THEN @levelBoost * 0.5 ELSE 0 END )
        """;

    public async Task<IReadOnlyList<AddressResult>> SearchAsync(ParsedAddressQuery query, CancellationToken ct)
    {
        if (!query.HasContent) return Array.Empty<AddressResult>();

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        // Транзакция ради SET LOCAL: порог similarity живёт только в её рамках, не течёт в пул.
        await using var tx = await conn.BeginTransactionAsync(ct);

        var threshold = Math.Clamp(query.SimilarityThreshold, 0.1, 1.0).ToString(CultureInfo.InvariantCulture);
        await conn.ExecuteAsync(new CommandDefinition(
            $"SET LOCAL pg_trgm.similarity_threshold = {threshold}",
            transaction: tx, cancellationToken: ct));

        // Базовое ограничение по явному parentId (если задан) — действует на все фазы.
        string? baseScope = null;
        if (query.ParentObjectId is { } pid)
        {
            var parentPath = await GetPathAsync(conn, tx, pid, ct);
            if (parentPath is null) return Array.Empty<AddressResult>();
            baseScope = parentPath + ".%";
        }

        // Эффективные фильтры: точный LevelFilter (legacy) имеет приоритет над диапазоном from/to.
        var levelFrom = query.LevelFilter ?? query.LevelFrom;
        var levelTo = query.LevelFilter ?? query.LevelTo;
        var regionCode = query.RegionCode;

        // --- Случай 1: ни улицы, ни дома → прямой гибридный поиск объекта.
        if (query.Street is null && query.House is null)
        {
            var term = query.RegionOrCity ?? (query.Normalized.Length >= 2 ? query.Normalized : null);
            if (term is null) return Array.Empty<AddressResult>();
            var direct = await SearchObjectsAsync(conn, tx, term, levelFrom, levelTo, regionCode, baseScope, query.Limit, ct);
            await tx.CommitAsync(ct);
            return direct;
        }

        // --- Фаза 1: контейнер (регион/город). Префикс его поддерева — для поиска улицы/дома.
        var containerPrefix = baseScope;
        if (query.RegionOrCity is not null)
        {
            var container = await ResolveBestAsync(conn, tx, query.RegionOrCity, baseScope, ct);
            if (container is { Path: { } cp })
                containerPrefix = cp + ".%";
        }

        // --- Фаза 2a: есть дом → ищем дома в самом узком известном поддереве.
        if (query.House is not null)
        {
            long? streetId = null;
            var housePrefix = containerPrefix;

            // Если названа улица — резолвим её и привязываемся к домам по parent_object_id (точно).
            if (query.Street is not null)
            {
                var street = await ResolveBestAsync(conn, tx, query.Street, containerPrefix, ct);
                if (street is { } s)
                {
                    streetId = s.ObjectId;
                    housePrefix = null; // точная привязка вместо префикса пути
                }
            }

            // Нужен хоть какой-то scope — иначе по всей базе домов искать нельзя.
            if (streetId is not null || housePrefix is not null)
            {
                var num = query.HouseNum ?? query.House!;
                var houses = await SearchHousesAsync(conn, tx, num, query.Building, streetId, housePrefix, query.Limit, ct);
                await tx.CommitAsync(ct);
                return houses;
            }
        }

        // --- Фаза 2b: дома нет (или негде искать) → перечисляем улицы в поддереве контейнера.
        var streetTerm = query.Street ?? query.RegionOrCity;
        if (streetTerm is null)
        {
            await tx.CommitAsync(ct);
            return Array.Empty<AddressResult>();
        }

        var results = await SearchObjectsAsync(conn, tx, streetTerm, levelFrom, levelTo, regionCode, containerPrefix, query.Limit, ct);
        await tx.CommitAsync(ct);
        return results;
    }

    /// <summary>Гибридный поиск по search.address_objects. pathPrefix != null — ограничение поддеревом;
    /// levelFrom/levelTo — диапазон уровней (from_bound/to_bound); regionCode — ограничение субъектом.</summary>
    private static async Task<IReadOnlyList<AddressResult>> SearchObjectsAsync(
        NpgsqlConnection conn, IDbTransaction tx,
        string term, int? levelFrom, int? levelTo, int? regionCode, string? pathPrefix, int limit, CancellationToken ct)
    {
        // Опциональные фильтры применяем на этапе отбора кандидатов (cand).
        var filters = new StringBuilder();
        if (levelFrom is not null) filters.AppendLine("  AND level >= @levelFrom");
        if (levelTo is not null) filters.AppendLine("  AND level <= @levelTo");
        if (regionCode is not null) filters.AppendLine("  AND region_code = @regionCode");
        if (pathPrefix is not null) filters.AppendLine("  AND path LIKE @pathPrefix");

        // RRF: кандидаты ранжируются отдельно по FTS и по триграммам (оконные row_number),
        // затем складываются обратные ранги. Несравнимые шкалы ts_rank/similarity не смешиваем.
        var sql = $"""
            WITH q AS (SELECT plainto_tsquery('russian', @term) AS tsq),
            cand AS (
                SELECT a.*,
                       ts_rank(a.name_tsv, q.tsq)::float8 AS fts_rank,
                       similarity(a.name, @term)::float8  AS trgm_sim,
                       (a.name_tsv @@ q.tsq)      AS fts_match,
                       (a.name % @term)           AS trgm_match
                FROM search.address_objects a
                CROSS JOIN q
                WHERE (a.name_tsv @@ q.tsq OR a.name % @term)
            {filters}
            ),
            ranked AS (
                SELECT *,
                       row_number() OVER (ORDER BY fts_rank DESC, object_id) AS r_fts,
                       row_number() OVER (ORDER BY trgm_sim DESC, object_id) AS r_trgm
                FROM cand
            )
            SELECT object_id   AS "ObjectId",
                   object_guid AS "ObjectGuid",
                   parent_guid AS "ParentGuid",
                   level       AS "Level",
                   name        AS "Name",
                   type_name   AS "TypeName",
                   full_name   AS "FullName",
                   region_code AS "RegionCode",
                   postal_code AS "PostalCode",
                   okato       AS "Okato",
                   oktmo       AS "Oktmo",
                   ifns_ul     AS "IfnsUl",
                   ifns_fl     AS "IfnsFl",
                   kladr_code  AS "KladrCode",
                   region      AS "Region",
                   area        AS "Area",
                   city        AS "City",
                   settlement  AS "Settlement",
                   street      AS "Street",
                   fts_rank    AS "FtsRank",
                   trgm_sim    AS "TrgmSimilarity",
                   ({RrfScoreExpr})::float8 AS "Score"
            FROM ranked
            ORDER BY "Score" DESC
            LIMIT @limit
            """;

        var rows = await conn.QueryAsync<AddressResult>(new CommandDefinition(
            sql,
            new { term, levelFrom, levelTo, regionCode, pathPrefix, limit, rrfK = RrfK, levelBoost = RrfLevelBoost },
            transaction: tx, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// Поиск домов. Привязка либо по <paramref name="parentObjectId"/> (точная улица — индекс-сик
    /// по parent_object_id), либо по <paramref name="pathPrefix"/> (поддерево города). Базовый номер
    /// матчится против house_num, корпус/строение — против add_num1/add_num2 (мягкий бонус к скору).
    /// </summary>
    private static async Task<IReadOnlyList<AddressResult>> SearchHousesAsync(
        NpgsqlConnection conn, IDbTransaction tx,
        string num, string? building, long? parentObjectId, string? pathPrefix, int limit, CancellationToken ct)
    {
        const string sql = """
            SELECT object_id                                   AS "ObjectId",
                   object_guid                                 AS "ObjectGuid",
                   parent_guid                                 AS "ParentGuid",
                   @houseLevel                                 AS "Level",
                   house_num                                   AS "Name",
                   NULL::text                                  AS "TypeName",
                   full_name                                   AS "FullName",
                   region_code                                 AS "RegionCode",
                   postal_code                                 AS "PostalCode",
                   okato                                       AS "Okato",
                   oktmo                                       AS "Oktmo",
                   ifns_ul                                     AS "IfnsUl",
                   ifns_fl                                     AS "IfnsFl",
                   kladr_code                                  AS "KladrCode",
                   region                                      AS "Region",
                   area                                        AS "Area",
                   city                                        AS "City",
                   settlement                                  AS "Settlement",
                   street                                      AS "Street",
                   0::float8                                   AS "FtsRank",
                   similarity(house_num, @num)::float8         AS "TrgmSimilarity",
                   ( CASE WHEN lower(house_num) = @numLower THEN 1.0
                          ELSE similarity(house_num, @num) END
                     + CASE WHEN @buildingLower IS NOT NULL
                                 AND (lower(add_num1) = @buildingLower OR lower(add_num2) = @buildingLower)
                            THEN 0.25 ELSE 0 END
                   )::float8                                   AS "Score"
            FROM search.houses
            WHERE house_num IS NOT NULL
              AND (@parentObjectId::bigint IS NULL OR parent_object_id = @parentObjectId)
              AND (@pathPrefix IS NULL OR path LIKE @pathPrefix)
              AND (lower(house_num) = @numLower OR house_num % @num)
            ORDER BY "Score" DESC, house_num
            LIMIT @limit
            """;

        var rows = await conn.QueryAsync<AddressResult>(new CommandDefinition(
            sql,
            new
            {
                num,
                numLower = num.ToLowerInvariant(),
                buildingLower = building?.ToLowerInvariant(),
                parentObjectId,
                pathPrefix,
                houseLevel = HouseLevel,
                limit,
            },
            transaction: tx, cancellationToken: ct));
        return rows.AsList();
    }

    private readonly record struct ResolveHit(long ObjectId, Guid? ObjectGuid, string? Path);

    /// <summary>Лучший объект по термину в заданном поддереве (с его денормализованным path).</summary>
    private static async Task<ResolveHit?> ResolveBestAsync(
        NpgsqlConnection conn, IDbTransaction tx, string term, string? pathPrefix, CancellationToken ct)
    {
        // То же RRF-ранжирование, что и в основном поиске, но нужен только топ-1 (с его path).
        var filter = pathPrefix is not null ? "  AND path LIKE @pathPrefix" : string.Empty;
        var sql = $"""
            WITH q AS (SELECT plainto_tsquery('russian', @term) AS tsq),
            cand AS (
                SELECT a.object_id, a.object_guid, a.path, a.level,
                       ts_rank(a.name_tsv, q.tsq) AS fts_rank,
                       similarity(a.name, @term)  AS trgm_sim,
                       (a.name_tsv @@ q.tsq)      AS fts_match,
                       (a.name % @term)           AS trgm_match
                FROM search.address_objects a
                CROSS JOIN q
                WHERE (a.name_tsv @@ q.tsq OR a.name % @term)
            {filter}
            ),
            ranked AS (
                SELECT *,
                       row_number() OVER (ORDER BY fts_rank DESC, object_id) AS r_fts,
                       row_number() OVER (ORDER BY trgm_sim DESC, object_id) AS r_trgm
                FROM cand
            )
            SELECT object_id AS "ObjectId", object_guid AS "ObjectGuid", path AS "Path"
            FROM ranked
            ORDER BY {RrfScoreExpr} DESC
            LIMIT 1
            """;

        var rows = await conn.QueryAsync<ResolveHit>(new CommandDefinition(
            sql,
            new { term, pathPrefix, rrfK = RrfK, levelBoost = RrfLevelBoost },
            transaction: tx, cancellationToken: ct));

        var hit = rows.FirstOrDefault();
        return hit.ObjectId == 0 ? null : hit; // object_id в ГАР никогда не 0 -> признак «не найдено»
    }

    /// <summary>Денормализованный path объекта в проекции (objectid.objectid…) или null.</summary>
    private static async Task<string?> GetPathAsync(
        NpgsqlConnection conn, IDbTransaction tx, long objectId, CancellationToken ct) =>
        await conn.QueryFirstOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT path FROM search.address_objects WHERE object_id = @objectId LIMIT 1",
            new { objectId }, transaction: tx, cancellationToken: ct));

    public async Task<AddressResult?> GetByGuidAsync(Guid guid, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        // Сначала адресообразующий объект, затем дом. Score/Sim = 1.0 (точное совпадение по id).
        const string aoSql = """
            SELECT object_id AS "ObjectId", object_guid AS "ObjectGuid", parent_guid AS "ParentGuid",
                   level AS "Level", name AS "Name", type_name AS "TypeName", full_name AS "FullName",
                   region_code AS "RegionCode", postal_code AS "PostalCode", okato AS "Okato", oktmo AS "Oktmo",
                   ifns_ul AS "IfnsUl", ifns_fl AS "IfnsFl", kladr_code AS "KladrCode",
                   region AS "Region", area AS "Area", city AS "City", settlement AS "Settlement", street AS "Street",
                   0::float8 AS "FtsRank", 1.0::float8 AS "TrgmSimilarity", 1.0::float8 AS "Score"
            FROM search.address_objects WHERE object_guid = @guid LIMIT 1
            """;
        var ao = await conn.QueryFirstOrDefaultAsync<AddressResult>(new CommandDefinition(aoSql, new { guid }, cancellationToken: ct));
        if (ao is not null) return ao;

        const string houseSql = """
            SELECT object_id AS "ObjectId", object_guid AS "ObjectGuid", parent_guid AS "ParentGuid",
                   @houseLevel AS "Level", house_num AS "Name", NULL::text AS "TypeName", full_name AS "FullName",
                   region_code AS "RegionCode", postal_code AS "PostalCode", okato AS "Okato", oktmo AS "Oktmo",
                   ifns_ul AS "IfnsUl", ifns_fl AS "IfnsFl", kladr_code AS "KladrCode",
                   region AS "Region", area AS "Area", city AS "City", settlement AS "Settlement", street AS "Street",
                   0::float8 AS "FtsRank", 1.0::float8 AS "TrgmSimilarity", 1.0::float8 AS "Score"
            FROM search.houses WHERE object_guid = @guid LIMIT 1
            """;
        return await conn.QueryFirstOrDefaultAsync<AddressResult>(
            new CommandDefinition(houseSql, new { guid, houseLevel = HouseLevel }, cancellationToken: ct));
    }
}
