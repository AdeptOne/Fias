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

    // Проекция строки результата из CTE `ranked` (общая для прямого поиска и денорм-фолбэка).
    // Константная интерполяция RrfScoreExpr допустима — он тоже const.
    private const string RankedSelect = $"""
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
        """;

    // Проекция строки дома (голые колонки, без алиаса таблицы) — общая для поиска по поддереву/
    // улице и для street-aware денорм-фолбэка. Параметры @num/@numLower/@buildingLower/@houseLevel
    // задаёт вызывающий. Точный номер = 1.0, вариант (литера/корпус) = 0.6; совпавший корпус +0.25.
    private const string HouseProjection = """
        object_id   AS "ObjectId",
        object_guid AS "ObjectGuid",
        parent_guid AS "ParentGuid",
        @houseLevel AS "Level",
        house_num   AS "Name",
        NULL::text  AS "TypeName",
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
        0::float8   AS "FtsRank",
        similarity(house_num, @num)::float8 AS "TrgmSimilarity",
        ( CASE WHEN lower(house_num) = @numLower THEN 1.0 ELSE 0.6 END
          + CASE WHEN @buildingLower IS NOT NULL
                      AND (lower(add_num1) = @buildingLower OR lower(add_num2) = @buildingLower)
                 THEN 0.25 ELSE 0 END
        )::float8   AS "Score"
        """;

    // FTS-выражение запроса. При наличии инициала («б хмельницкого» → «б:* & хмельницкого»)
    // идём в to_tsquery с префиксным матчем (ловит и «богдан», и квалификатор «большая»),
    // иначе — привычный plainto_tsquery. @tsqExpr = null → поведение идентично прежнему (ноль
    // регрессий для запросов без инициалов).
    private const string TsQueryExpr =
        "CASE WHEN @tsqExpr::text IS NULL THEN plainto_tsquery('russian', @term) " +
        "ELSE to_tsquery('russian', @tsqExpr) END";

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
        ResolveHit? container = null;
        var containerPrefix = baseScope;
        if (query.RegionOrCity is not null)
        {
            container = await ResolveBestAsync(conn, tx, query.RegionOrCity, baseScope, ct);
            if (container is { Path: { } cp })
                containerPrefix = cp + ".%";
        }

        // --- Фаза 2a: есть дом → ищем дома в самом узком известном поддереве.
        if (query.House is not null)
        {
            var num = query.HouseNum ?? query.House!;

            // «улица + дом» без явного города («труда 11», «ленина 5»): голое «имя + номер» —
            // это почти всегда улица + дом. Ищем дом N по ВСЕМ одноимённым улицам (многогородной
            // поиск, имя улицы фильтруется в самом запросе), крупные города первыми.
            //
            // Раньше ветка включалась лишь когда ЛУЧШИЙ резолв имени сам оказывался улицей
            // (container.Level == 8). Но одноимённый НП глушил её: «Новый» резолвился в п. Новый
            // (бонус уровня поднимает НП над улицей), ветка пропускалась, и дом искался по
            // поддереву посёлка — а там SearchHouses без улицы возвращает дом N на ЛЮБОЙ улице
            // (Пионерская 6 вместо ул. Новый 6). Поэтому гейт по уровню убран: пробуем
            // многогородной поиск всегда; контейнер-резолв ниже остаётся фолбэком на пустой выдаче
            // (реально сельский дом прямо под НП, где одноимённой улицы нет).
            if (query.Street is null && query.RegionOrCity is not null)
            {
                var multi = await SearchHousesAcrossStreetsAsync(
                    conn, tx, query.RegionOrCity, num, query.Building, regionCode, baseScope, query.Limit, ct);
                if (multi.Count > 0)
                {
                    await tx.CommitAsync(ct);
                    return multi;
                }
            }

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

            // 1) Точная привязка к резолвнутой улице (индекс-сик по parent_object_id) — лучший путь.
            if (streetId is not null)
            {
                var houses = await SearchHousesAsync(conn, tx, num, query.Building, streetId, null, query.Limit, ct);
                if (houses.Count > 0)
                {
                    await tx.CommitAsync(ct);
                    return houses;
                }
            }

            // 2) Улица названа, но точная привязка не дала дома (гомоним НП увёл сужение в чужое
            //    поддерево; многословное имя ушло мимо границы разбора). Street-aware денорм-поиск:
            //    имя улицы + контейнер ПРЯМО по денорм-полям дома (homonym-proof), с перебором
            //    границы «контейнер|улица» по NameTokens (как фолбэк улиц в фазе 2b).
            if (query.Street is not null && query.RegionOrCity is not null && query.NameTokens.Count >= 2)
            {
                var toks = query.NameTokens;
                for (var k = 1; k < toks.Count; k++)
                {
                    var contName = string.Join(' ', toks.Take(k));
                    var streetName = string.Join(' ', toks.Skip(k));
                    var byName = await SearchHousesByContainerStreetAsync(
                        conn, tx, streetName, contName, num, query.Building, regionCode, query.Limit, ct);
                    if (byName.Count > 0)
                    {
                        await tx.CommitAsync(ct);
                        return byName;
                    }
                }
            }

            // 3) Последний резерв — слепой поиск дома по поддереву контейнера (улицы нет / денорм пуст).
            if (housePrefix is not null)
            {
                var houses = await SearchHousesAsync(conn, tx, num, query.Building, null, housePrefix, query.Limit, ct);
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

        // Recall-фолбэк «контейнер + улица»: сужение по дереву дало ПУСТО (контейнер-резолв
        // промахнулся — гомоним НП или многословное имя ушло мимо границы разбора). Перебираем
        // границу «контейнер|улица» по сырым NameTokens и ищем улицу с проверкой контейнера ПРЯМО
        // по денорм-полю строки (homonym-proof). Только на пустой основной выдаче → 0 регрессий.
        if (results.Count == 0 && query.RegionOrCity is not null && query.NameTokens.Count >= 2)
        {
            var toks = query.NameTokens;
            // Перебор границы «контейнер|улица» в ОБЕ стороны: прямой порядок (город слева,
            // «Верхний Уфалей Пугачёва») и обратный (reorder — город справа, «Пугачёва Верхний
            // Уфалей»). Денорм-контейнер homonym-proof, неверная раскладка просто даёт пусто.
            for (var k = 1; k < toks.Count && results.Count == 0; k++)
            {
                var left = string.Join(' ', toks.Take(k));
                var right = string.Join(' ', toks.Skip(k));
                results = await SearchStreetsByContainerAsync(conn, tx, right, left, regionCode, query.Limit, ct);
                if (results.Count == 0)
                    results = await SearchStreetsByContainerAsync(conn, tx, left, right, regionCode, query.Limit, ct);
            }
        }

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
            WITH q AS (SELECT {TsQueryExpr} AS tsq),
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
                       rank() OVER (ORDER BY fts_rank DESC) AS r_fts,
                       rank() OVER (ORDER BY trgm_sim DESC) AS r_trgm
                FROM cand
            )
            {RankedSelect}
            -- Популярность (house_count) — только тай-брейк при равном RRF: разруливает дубли
            -- одноимённых улиц (берём «живую» с домами), но НИКОГДА не перебивает лучшее совпадение.
            ORDER BY "Score" DESC, house_count DESC NULLS LAST, object_id
            LIMIT @limit
            """;

        var rows = await conn.QueryAsync<AddressResult>(new CommandDefinition(
            sql,
            new { term, tsqExpr = BuildPrefixTsQuery(term), levelFrom, levelTo, regionCode, pathPrefix, limit, rrfK = RrfK, levelBoost = RrfLevelBoost },
            transaction: tx, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// Recall-фолбэк «контейнер + улица» (без дома), когда поуровневое сужение дало ПУСТО.
    /// Сужение ломается, когда контейнер-резолв промахнулся: гомоним НП («Боровое» — 4 села,
    /// выбрали не то → жёсткий LIKE 'path.%' отсёк эталон) или многословное имя НП ушло мимо
    /// границы разбора («Верхний Уфалей Пугачёва» → контейнер «верхний», улица «уфалей пугачёва»).
    ///
    /// Здесь имя улицы матчится по индексам (name_tsv/trgm), а контейнер проверяется ПРЯМО по
    /// денормализованным city/settlement/area самой строки улицы — homonym-proof, без сужения по
    /// дереву. Точную границу «контейнер|улица» вызывающий перебирает (NameTokens), беря первый
    /// непустой результат.
    /// </summary>
    private static async Task<IReadOnlyList<AddressResult>> SearchStreetsByContainerAsync(
        NpgsqlConnection conn, IDbTransaction tx,
        string street, string container, int? regionCode, int limit, CancellationToken ct)
    {
        var filter = regionCode is not null ? "  AND region_code = @regionCode" : string.Empty;
        var sql = $"""
            WITH q AS (SELECT {TsQueryExpr} AS tsq),
            cand AS (
                SELECT a.*,
                       ts_rank(a.name_tsv, q.tsq)::float8 AS fts_rank,
                       similarity(a.name, @term)::float8  AS trgm_sim,
                       (a.name_tsv @@ q.tsq)      AS fts_match,
                       (a.name % @term)           AS trgm_match
                FROM search.address_objects a
                CROSS JOIN q
                WHERE a.level = 8
                  AND (a.name_tsv @@ q.tsq OR a.name % @term)
                  -- Контейнер — по родному денорм-полю улицы (точное равенство имени НП/района).
                  AND ( lower(btrim(a.city))       = @container
                     OR lower(btrim(a.settlement)) = @container
                     OR lower(btrim(a.area))       = @container )
            {filter}
            ),
            ranked AS (
                SELECT *,
                       rank() OVER (ORDER BY fts_rank DESC) AS r_fts,
                       rank() OVER (ORDER BY trgm_sim DESC) AS r_trgm
                FROM cand
            )
            {RankedSelect}
            ORDER BY "Score" DESC, house_count DESC NULLS LAST, object_id
            LIMIT @limit
            """;

        var rows = await conn.QueryAsync<AddressResult>(new CommandDefinition(
            sql,
            new { term = street, tsqExpr = BuildPrefixTsQuery(street), container, regionCode, limit, rrfK = RrfK, levelBoost = RrfLevelBoost },
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
        var sql = $"""
            SELECT {HouseProjection}
            FROM search.houses
            WHERE house_num IS NOT NULL
              AND (@parentObjectId::bigint IS NULL OR parent_object_id = @parentObjectId)
              AND (@pathPrefix IS NULL OR path LIKE @pathPrefix)
              AND (lower(house_num) = @numLower OR house_num ~* @numVariant)
            ORDER BY "Score" DESC, house_num, object_id
            LIMIT @limit
            """;

        var rows = await conn.QueryAsync<AddressResult>(new CommandDefinition(
            sql,
            new
            {
                num,
                numLower = num.ToLowerInvariant(),
                numVariant = $"^{num}(\\D|$)",
                buildingLower = building?.ToLowerInvariant(),
                parentObjectId,
                pathPrefix,
                houseLevel = HouseLevel,
                limit,
            },
            transaction: tx, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// Street-aware денорм-фолбэк для «контейнер + улица + дом», когда точная привязка к улице не
    /// дала дома: гомоним НП увёл сужение в чужое поддерево, и слепой поиск по поддереву вернул бы
    /// дом N на ЛЮБОЙ улице (Березовка «Мичурина 9» → ул. Центральная д9). Здесь дом матчится по
    /// номеру (индекс house_num) и ПРЯМО по денорм-полям самого дома: точное имя улицы + контейнер
    /// (city/settlement/area) — homonym-proof, без сужения по дереву.
    /// </summary>
    private static async Task<IReadOnlyList<AddressResult>> SearchHousesByContainerStreetAsync(
        NpgsqlConnection conn, IDbTransaction tx,
        string street, string container, string num, string? building, int? regionCode, int limit, CancellationToken ct)
    {
        var filter = regionCode is not null ? "  AND region_code = @regionCode" : string.Empty;
        var sql = $"""
            SELECT {HouseProjection}
            FROM search.houses
            WHERE house_num IS NOT NULL
              AND (lower(house_num) = @numLower OR house_num ~* @numVariant)
              AND lower(btrim(street)) = @street
              AND ( lower(btrim(city))       = @container
                 OR lower(btrim(settlement)) = @container
                 OR lower(btrim(area))       = @container )
            {filter}
            ORDER BY "Score" DESC, house_num, object_id
            LIMIT @limit
            """;

        var rows = await conn.QueryAsync<AddressResult>(new CommandDefinition(
            sql,
            new
            {
                num,
                numLower = num.ToLowerInvariant(),
                numVariant = $"^{num}(\\D|$)",
                buildingLower = building?.ToLowerInvariant(),
                street,
                container,
                regionCode,
                houseLevel = HouseLevel,
                limit,
            },
            transaction: tx, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// Многогородной поиск дома: дом <paramref name="num"/> по ВСЕМ улицам, чьё имя совпадает с
    /// <paramref name="term"/> (кейс «труда 11» без города → дом 11 на Труда в разных городах).
    /// Точный номер строго выше вариантов с литерой/корпусом; порядок улиц — по популярности
    /// (house_count), чтобы крупные города шли первыми.
    /// </summary>
    private static async Task<IReadOnlyList<AddressResult>> SearchHousesAcrossStreetsAsync(
        NpgsqlConnection conn, IDbTransaction tx,
        string term, string num, string? building, int? regionCode, string? pathPrefix, int limit, CancellationToken ct)
    {
        var streetFilters = new StringBuilder();
        if (regionCode is not null) streetFilters.AppendLine("              AND a.region_code = @regionCode");
        if (pathPrefix is not null) streetFilters.AppendLine("              AND a.path LIKE @pathPrefix");

        var sql = $"""
            WITH q AS (SELECT {TsQueryExpr} AS tsq),
            streets AS (
                SELECT a.object_id, a.house_count,
                       -- Точное равенство имени улицы термину — лучшая трактовка, чем стеммингом
                       -- притянутый суперстринг («Береговая» vs «Береговая Ветлужская»,
                       -- «Комсомольская» vs «Комсомольский»). Поднимаем такие улицы в порядке.
                       (lower(btrim(a.name)) = lower(@term)) AS exact_name
                FROM search.address_objects a
                CROSS JOIN q
                WHERE a.level = 8
                  -- Именно НАЗВАННАЯ улица: FTS (стемминг) либо точное равенство. БЕЗ триграммного
                  -- %, который в этой ветке тянул близкие имена (Трудовая попадала в «труда»).
                  AND (a.name_tsv @@ q.tsq OR lower(btrim(a.name)) = lower(@term))
            {streetFilters}
            )
            SELECT h.object_id   AS "ObjectId",
                   h.object_guid AS "ObjectGuid",
                   h.parent_guid AS "ParentGuid",
                   @houseLevel   AS "Level",
                   h.house_num   AS "Name",
                   NULL::text    AS "TypeName",
                   h.full_name   AS "FullName",
                   h.region_code AS "RegionCode",
                   h.postal_code AS "PostalCode",
                   h.okato       AS "Okato",
                   h.oktmo       AS "Oktmo",
                   h.ifns_ul     AS "IfnsUl",
                   h.ifns_fl     AS "IfnsFl",
                   h.kladr_code  AS "KladrCode",
                   h.region      AS "Region",
                   h.area        AS "Area",
                   h.city        AS "City",
                   h.settlement  AS "Settlement",
                   h.street      AS "Street",
                   0::float8     AS "FtsRank",
                   similarity(h.house_num, @num)::float8 AS "TrgmSimilarity",
                   ( CASE WHEN lower(h.house_num) = @numLower THEN 1.0 ELSE 0.6 END
                     + CASE WHEN @buildingLower IS NOT NULL
                                 AND (lower(h.add_num1) = @buildingLower OR lower(h.add_num2) = @buildingLower)
                            THEN 0.25 ELSE 0 END )::float8 AS "Score"
            FROM search.houses h
            JOIN streets s ON s.object_id = h.parent_object_id
            WHERE h.house_num IS NOT NULL
              AND (lower(h.house_num) = @numLower OR h.house_num ~* @numVariant)
            -- Точный номер выше вариантов; среди равных — точное имя улицы выше суперстрингов,
            -- затем крупные улицы (house_count) первыми.
            ORDER BY "Score" DESC, s.exact_name DESC, s.house_count DESC NULLS LAST, h.house_num, h.object_id
            LIMIT @limit
            """;

        var rows = await conn.QueryAsync<AddressResult>(new CommandDefinition(
            sql,
            new
            {
                term,
                tsqExpr = BuildPrefixTsQuery(term),
                num,
                numLower = num.ToLowerInvariant(),
                numVariant = $"^{num}(\\D|$)",
                buildingLower = building?.ToLowerInvariant(),
                regionCode,
                pathPrefix,
                houseLevel = HouseLevel,
                limit,
            },
            transaction: tx, cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>
    /// Строит выражение для <c>to_tsquery</c> с префиксным матчем одиночных инициалов:
    /// «б хмельницкого» → «б:* &amp; хмельницкого». Возвращает null, если инициалов нет — тогда
    /// SQL остаётся на plainto_tsquery (ноль регрессий). Одиночная кириллическая буква = инициал
    /// имени («Б.Хмельницкого» → «Богдана Хмельницкого»); префикс ловит и квалификаторы
    /// («Б. Никитская» → «Большая»). Токены чистим до букв/цифр — вход to_tsquery строго валиден.
    /// </summary>
    private static string? BuildPrefixTsQuery(string term)
    {
        var parts = new List<string>();
        var hasInitial = false;

        foreach (var token in term.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var clean = new string(token.Where(char.IsLetterOrDigit).ToArray());
            if (clean.Length == 0) continue;

            if (clean.Length == 1 && clean[0] is >= 'а' and <= 'я')
            {
                parts.Add(clean + ":*");
                hasInitial = true;
            }
            else
            {
                parts.Add(clean);
            }
        }

        return hasInitial && parts.Count > 0 ? string.Join(" & ", parts) : null;
    }

    private readonly record struct ResolveHit(long ObjectId, Guid? ObjectGuid, string? Path, int? Level);

    /// <summary>Лучший объект по термину в заданном поддереве (с его денормализованным path).</summary>
    private static async Task<ResolveHit?> ResolveBestAsync(
        NpgsqlConnection conn, IDbTransaction tx, string term, string? pathPrefix, CancellationToken ct)
    {
        // То же RRF-ранжирование, что и в основном поиске, но нужен только топ-1 (с его path).
        var filter = pathPrefix is not null ? "  AND path LIKE @pathPrefix" : string.Empty;
        var sql = $"""
            WITH q AS (SELECT {TsQueryExpr} AS tsq),
            cand AS (
                SELECT a.object_id, a.object_guid, a.path, a.level, a.house_count,
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
                       rank() OVER (ORDER BY fts_rank DESC) AS r_fts,
                       rank() OVER (ORDER BY trgm_sim DESC) AS r_trgm
                FROM cand
            )
            SELECT object_id AS "ObjectId", object_guid AS "ObjectGuid", path AS "Path", level AS "Level"
            FROM ranked
            ORDER BY {RrfScoreExpr} DESC, house_count DESC NULLS LAST, object_id
            LIMIT 1
            """;

        var rows = await conn.QueryAsync<ResolveHit>(new CommandDefinition(
            sql,
            new { term, tsqExpr = BuildPrefixTsQuery(term), pathPrefix, rrfK = RrfK, levelBoost = RrfLevelBoost },
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
