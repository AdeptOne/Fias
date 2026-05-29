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
/// Ранжирование комбинирует два сигнала:
///   1) FTS (name_tsv + ts_rank) — точные/морфологические совпадения, ГЛАВНЫЙ сигнал;
///   2) pg_trgm similarity — фолбэк на опечатки, вес заведомо НИЖЕ FTS.
/// Плюс бонус за уровень: крупные города (level 4/5) поднимаются над одноимёнными сёлами.
///
/// Поуровневое сужение (stage 2/3) идёт без единого JOIN:
///   • контейнер (регион/город) ищется в search.address_objects;
///   • улица — в его поддереве по денормализованному столбцу path (LIKE 'prefix.%');
///   • дом — по search.houses.parent_object_id = <улица> (индекс-сик), что и решает проблему
///     медленных JOIN при десятках млн домов. full_name и parent_guid уже лежат в строке.
/// </summary>
public sealed class AddressSearchRepository(NpgsqlDataSource dataSource) : IAddressSearchRepository
{
    // FtsBonus делает любое FTS-совпадение строго «тяжелее» чисто триграммного.
    private const double FtsBonus = 1.0;
    private const double TrgmWeight = 0.3;
    private const double CityBoost = 0.15;

    private const int HouseLevel = 10;

    /// <summary>Выражение комбинированного скора — общее для ранжирования и для ORDER BY.</summary>
    private const string ScoreExpr = """
        ( CASE WHEN name_tsv @@ q.tsq THEN @ftsBonus + ts_rank(name_tsv, q.tsq) ELSE 0 END
          + similarity(name, @term) * @trgmWeight
          + CASE level WHEN 4 THEN @cityBoost WHEN 5 THEN @cityBoost WHEN 6 THEN @cityBoost * 0.5 ELSE 0 END )
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

        // --- Случай 1: ни улицы, ни дома → прямой гибридный поиск объекта.
        if (query.Street is null && query.House is null)
        {
            var term = query.RegionOrCity ?? (query.Normalized.Length >= 2 ? query.Normalized : null);
            if (term is null) return Array.Empty<AddressResult>();
            var direct = await SearchObjectsAsync(conn, tx, term, query.LevelFilter, baseScope, query.Limit, ct);
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

        var results = await SearchObjectsAsync(conn, tx, streetTerm, query.LevelFilter, containerPrefix, query.Limit, ct);
        await tx.CommitAsync(ct);
        return results;
    }

    /// <summary>Гибридный поиск по search.address_objects. pathPrefix != null — ограничение поддеревом.</summary>
    private static async Task<IReadOnlyList<AddressResult>> SearchObjectsAsync(
        NpgsqlConnection conn, IDbTransaction tx,
        string term, int? level, string? pathPrefix, int limit, CancellationToken ct)
    {
        var sql = new StringBuilder();
        sql.AppendLine("WITH q AS (SELECT plainto_tsquery('russian', @term) AS tsq)");
        sql.AppendLine($"""
            SELECT object_id                  AS "ObjectId",
                   object_guid                AS "ObjectGuid",
                   parent_guid                AS "ParentGuid",
                   level                      AS "Level",
                   name                       AS "Name",
                   type_name                  AS "TypeName",
                   full_name                  AS "FullName",
                   region_code                AS "RegionCode",
                   postal_code                AS "PostalCode",
                   okato                      AS "Okato",
                   oktmo                      AS "Oktmo",
                   ifns_ul                    AS "IfnsUl",
                   ifns_fl                    AS "IfnsFl",
                   kladr_code                 AS "KladrCode",
                   region                     AS "Region",
                   area                       AS "Area",
                   city                       AS "City",
                   settlement                 AS "Settlement",
                   street                     AS "Street",
                   ts_rank(name_tsv, q.tsq)   AS "FtsRank",
                   similarity(name, @term)    AS "TrgmSimilarity",
                   {ScoreExpr}                AS "Score"
            FROM search.address_objects
            CROSS JOIN q
            WHERE (name_tsv @@ q.tsq OR name % @term)
            """);
        if (level is not null)
            sql.AppendLine("  AND level = @level");
        if (pathPrefix is not null)
            sql.AppendLine("  AND path LIKE @pathPrefix");
        sql.AppendLine("""
            ORDER BY "Score" DESC
            LIMIT @limit
            """);

        var rows = await conn.QueryAsync<AddressResult>(new CommandDefinition(
            sql.ToString(),
            new { term, level, pathPrefix, limit, ftsBonus = FtsBonus, trgmWeight = TrgmWeight, cityBoost = CityBoost },
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
                   similarity(house_num, @num)                 AS "TrgmSimilarity",
                   ( CASE WHEN lower(house_num) = @numLower THEN 1.0
                          ELSE similarity(house_num, @num) END
                     + CASE WHEN @buildingLower IS NOT NULL
                                 AND (lower(add_num1) = @buildingLower OR lower(add_num2) = @buildingLower)
                            THEN 0.25 ELSE 0 END
                   )                                           AS "Score"
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
        var sql = new StringBuilder();
        sql.AppendLine("WITH q AS (SELECT plainto_tsquery('russian', @term) AS tsq)");
        sql.AppendLine("""
            SELECT object_id AS "ObjectId", object_guid AS "ObjectGuid", path AS "Path"
            FROM search.address_objects
            CROSS JOIN q
            WHERE (name_tsv @@ q.tsq OR name % @term)
            """);
        if (pathPrefix is not null)
            sql.AppendLine("  AND path LIKE @pathPrefix");
        sql.AppendLine($"ORDER BY {ScoreExpr} DESC");
        sql.AppendLine("LIMIT 1");

        var rows = await conn.QueryAsync<ResolveHit>(new CommandDefinition(
            sql.ToString(),
            new { term, pathPrefix, ftsBonus = FtsBonus, trgmWeight = TrgmWeight, cityBoost = CityBoost },
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
}
