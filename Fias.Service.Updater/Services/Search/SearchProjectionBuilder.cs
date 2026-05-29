using Fias.Service.Updater.Services.Db;
using Fias.Service.Updater.Services.Progress;
using Npgsql;

namespace Fias.Service.Updater.Services.Search;

public interface ISearchProjectionBuilder
{
    /// <summary>Полностью пересобрать денормализованный поисковый слой search.* из сырых fias.*.</summary>
    Task RebuildAsync(IProgressSink progress, CancellationToken ct);
}

/// <summary>
/// Наполняет денормализованную проекцию для поиска. Запускается ПОСЛЕ импорта сырых данных.
///
/// Пересборка разбита на наблюдаемые фазы (с прогресс-баром и счётчиками строк), т.к. операция
/// тяжёлая — особенно сборка реквизитов из fias.params (десятки млн строк):
///   снять индексы → типы → адресообразующие объекты → дома → построить индексы → ANALYZE.
/// Порядок важен: дома берут полный путь/реквизиты-фолбэк у уже наполненных address_objects.
/// Индексы снимаются на время загрузки и строятся разом в конце — кратно быстрее, чем поддерживать
/// GiST/GIN на каждой вставке.
///
/// В v1 — полная пересборка (и после full, и после delta): просто и всегда консистентно.
/// Инкрементальное обновление проекции по изменённым objectid — возможная оптимизация позже.
/// </summary>
public class SearchProjectionBuilder(
    INpgsqlConnectionFactory factory,
    ILogger<SearchProjectionBuilder> logger) : ISearchProjectionBuilder
{
    public async Task RebuildAsync(IProgressSink progress, CancellationToken ct)
    {
        logger.LogInformation("Пересборка денормализованного поискового слоя search.*");
        var bar = progress.StartProgressBar("Пересборка search.*");

        await using var conn = await factory.OpenAsync(ct);

        await ExecAsync(conn, DropIndexesSql, ct);
        progress.WriteLine("search.*: вторичные индексы сняты");
        bar.SetValue(10);

        var types = await ExecAsync(conn, TypesSql, ct);
        progress.WriteLine($"search.address_object_types: {types} строк");
        bar.SetValue(20);

        var objects = await ExecAsync(conn, AddressObjectsSql, ct);
        progress.WriteLine($"search.address_objects: {objects} строк");
        bar.SetValue(55);

        var houses = await ExecAsync(conn, HousesSql, ct);
        progress.WriteLine($"search.houses: {houses} строк");
        bar.SetValue(80);

        await ExecAsync(conn, CreateIndexesSql, ct);
        progress.WriteLine("search.*: индексы построены");
        bar.SetValue(95);

        await ExecAsync(conn, AnalyzeSql, ct);
        bar.SetValue(100);
        progress.WriteLine("search.*: ANALYZE выполнен, пересборка завершена");

        logger.LogInformation(
            "Поисковый слой search.* пересобран (объектов: {Objects}, домов: {Houses})", objects, houses);
    }

    /// <summary>Выполнить фазу. CommandTimeout=0 — фазы по всей базе ГАР длительные.</summary>
    private static async Task<int> ExecAsync(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 0 };
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    // Сносим вторичные индексы перед загрузкой (PK не трогаем). IF EXISTS — первый запуск ок.
    private const string DropIndexesSql = """
        DROP INDEX IF EXISTS search.ix_search_ao_name_tsv;
        DROP INDEX IF EXISTS search.ix_search_ao_name_trgm;
        DROP INDEX IF EXISTS search.ix_search_ao_parent;
        DROP INDEX IF EXISTS search.ix_search_ao_region;
        DROP INDEX IF EXISTS search.ix_search_ao_guid;
        DROP INDEX IF EXISTS search.ix_search_ao_path;
        DROP INDEX IF EXISTS search.ix_search_houses_parent;
        DROP INDEX IF EXISTS search.ix_search_houses_path;
        DROP INDEX IF EXISTS search.ix_search_houses_num_trgm;
        DROP INDEX IF EXISTS search.ix_search_houses_guid;
        """;

    // Строим индексы уже по наполненным таблицам — кратно быстрее, чем поддерживать их на вставке.
    private const string CreateIndexesSql = """
        -- GIN по tsvector — точный/морфологический FTS (оператор @@).
        CREATE INDEX ix_search_ao_name_tsv ON search.address_objects USING gin (name_tsv);
        -- GiST по триграммам — нечёткий поиск опечаток. GiST (а не GIN) выбран осознанно:
        -- поддерживает KNN `name <-> :q` (top-N по близости прямо из индекса) и компактнее.
        -- Для чисто фильтрующего сценария можно заменить на USING gin (name gin_trgm_ops).
        CREATE INDEX ix_search_ao_name_trgm ON search.address_objects USING gist (name gist_trgm_ops);
        CREATE INDEX ix_search_ao_parent ON search.address_objects (parent_object_id);
        CREATE INDEX ix_search_ao_region ON search.address_objects (region_object_id);
        CREATE INDEX ix_search_ao_guid ON search.address_objects (object_guid);
        CREATE INDEX ix_search_ao_path ON search.address_objects (path text_pattern_ops);
        -- Ключ против «медленных JOIN по десяткам млн домов»: дом в улице = индекс-сик по parent.
        CREATE INDEX ix_search_houses_parent ON search.houses (parent_object_id);
        CREATE INDEX ix_search_houses_path ON search.houses (path text_pattern_ops);
        CREATE INDEX ix_search_houses_num_trgm ON search.houses USING gin (house_num gin_trgm_ops);
        CREATE INDEX ix_search_houses_guid ON search.houses (object_guid);
        """;

    // Свежая статистика — иначе планировщик промахнётся с BitmapOr(FTS, trgm)/выбором индексов.
    private const string AnalyzeSql = """
        ANALYZE search.address_object_types;
        ANALYZE search.address_objects;
        ANALYZE search.houses;
        """;

    // --- Фаза «типы» ---------------------------------------------------------
    private const string TypesSql = """
        TRUNCATE search.address_object_types;
        INSERT INTO search.address_object_types (id, level, short_name, name)
        SELECT id, level, shortname, name
          FROM fias.addressobject_types;
        """;

    // --- Фаза «адресообразующие объекты»: полный путь + структурный сплит + реквизиты --------
    //     DISTINCT ON (objectid) — страховка от исторических дублей (PK проекции — object_id).
    private const string AddressObjectsSql = """
        TRUNCATE search.address_objects;
        INSERT INTO search.address_objects
            (object_id, object_guid, parent_object_id, parent_guid, region_object_id, path,
             level, type_name, name, full_name,
             region_code, postal_code, okato, oktmo, ifns_ul, ifns_fl, kladr_code,
             region, area, city, settlement, street)
        WITH ah AS (
            SELECT DISTINCT ON (objectid) objectid, parentobjid, path
              FROM fias.adm_hierarchy
             WHERE isactive = true
             ORDER BY objectid, id DESC
        ),
        ao AS (
            SELECT DISTINCT ON (objectid) objectid, objectguid, name, typename, level
              FROM fias.addressobjects
             WHERE isactual = true AND isactive = true
             ORDER BY objectid, id DESC
        ),
        -- Текущее значение каждого нужного параметра (typeid: 5 индекс, 6 ОКАТО, 7 ОКТМО,
        -- 2 ИФНС ЮЛ, 1 ИФНС ФЛ, 11 КЛАДР, 12 код региона). objectid уникален -> джойн без objtype.
        prm AS (
            SELECT DISTINCT ON (objectid, typeid) objectid, typeid, btrim(value) AS value
              FROM fias.params
             WHERE typeid IN (1, 2, 5, 6, 7, 11, 12)
               AND (enddate IS NULL OR enddate > current_date)
             ORDER BY objectid, typeid, startdate DESC NULLS LAST, id DESC
        ),
        pv AS (
            SELECT objectid,
                   max(value) FILTER (WHERE typeid = 12) AS region_code,
                   max(value) FILTER (WHERE typeid = 5)  AS postal_code,
                   max(value) FILTER (WHERE typeid = 6)  AS okato,
                   max(value) FILTER (WHERE typeid = 7)  AS oktmo,
                   max(value) FILTER (WHERE typeid = 2)  AS ifns_ul,
                   max(value) FILTER (WHERE typeid = 1)  AS ifns_fl,
                   max(value) FILTER (WHERE typeid = 11) AS kladr_code
              FROM prm GROUP BY objectid
        ),
        -- Один проход по пути: и full_name (с порядком), и сплит по уровням ГАР.
        expand AS (
            SELECT ah.objectid, e.ord, anc.name, anc.typename, anc.level
              FROM ah
              CROSS JOIN LATERAL unnest(string_to_array(ah.path, '.')) WITH ORDINALITY AS e(anc_objectid, ord)
              JOIN ao anc ON anc.objectid = e.anc_objectid::bigint
        ),
        agg AS (
            SELECT objectid,
                   string_agg(btrim(coalesce(typename, '') || ' ' || coalesce(name, '')), ', ' ORDER BY ord) AS full_name,
                   max(name) FILTER (WHERE level = 1)       AS region,
                   max(name) FILTER (WHERE level IN (2, 3)) AS area,
                   max(name) FILTER (WHERE level = 5)       AS city,
                   max(name) FILTER (WHERE level IN (4, 6)) AS settlement,
                   max(name) FILTER (WHERE level = 8)       AS street
              FROM expand GROUP BY objectid
        )
        SELECT a.objectid,
               a.objectguid,
               ah.parentobjid,
               p.objectguid,
               split_part(ah.path, '.', 1)::bigint,
               ah.path,
               a.level,
               a.typename,
               a.name,
               g.full_name,
               CASE WHEN pv.region_code ~ '^\d+$' THEN pv.region_code::integer END,
               pv.postal_code, pv.okato, pv.oktmo, pv.ifns_ul, pv.ifns_fl, pv.kladr_code,
               g.region, g.area, g.city, g.settlement, g.street
          FROM ao a
          JOIN ah ON ah.objectid = a.objectid
          LEFT JOIN ao p ON p.objectid = ah.parentobjid
          LEFT JOIN agg g ON g.objectid = a.objectid
          LEFT JOIN pv ON pv.objectid = a.objectid;
        """;

    // --- Фаза «дома»: привязка к родителю; реквизиты — свои (params) с фолбэком на родителя,
    //     структурный сплит наследуется от родителя, house = house_num. -----------------------
    private const string HousesSql = """
        TRUNCATE search.houses;
        INSERT INTO search.houses
            (object_id, object_guid, parent_object_id, parent_guid, path,
             house_num, add_num1, add_num2, house_type, add_type1, add_type2, full_name,
             region_code, postal_code, okato, oktmo, ifns_ul, ifns_fl, kladr_code,
             region, area, city, settlement, street)
        WITH ah AS (
            SELECT DISTINCT ON (objectid) objectid, parentobjid, path
              FROM fias.adm_hierarchy
             WHERE isactive = true
             ORDER BY objectid, id DESC
        ),
        hs AS (
            SELECT DISTINCT ON (objectid)
                   objectid, objectguid, housenum, addnum1, addnum2, housetype, addtype1, addtype2
              FROM fias.houses
             WHERE isactual = true AND isactive = true
             ORDER BY objectid, id DESC
        ),
        prm AS (
            SELECT DISTINCT ON (objectid, typeid) objectid, typeid, btrim(value) AS value
              FROM fias.params
             WHERE typeid IN (1, 2, 5, 6, 7, 11, 12)
               AND (enddate IS NULL OR enddate > current_date)
             ORDER BY objectid, typeid, startdate DESC NULLS LAST, id DESC
        ),
        pv AS (
            SELECT objectid,
                   max(value) FILTER (WHERE typeid = 12) AS region_code,
                   max(value) FILTER (WHERE typeid = 5)  AS postal_code,
                   max(value) FILTER (WHERE typeid = 6)  AS okato,
                   max(value) FILTER (WHERE typeid = 7)  AS oktmo,
                   max(value) FILTER (WHERE typeid = 2)  AS ifns_ul,
                   max(value) FILTER (WHERE typeid = 1)  AS ifns_fl,
                   max(value) FILTER (WHERE typeid = 11) AS kladr_code
              FROM prm GROUP BY objectid
        )
        SELECT h.objectid,
               h.objectguid,
               ah.parentobjid,
               p.object_guid,
               ah.path,
               h.housenum,
               h.addnum1,
               h.addnum2,
               h.housetype,
               h.addtype1,
               h.addtype2,
               coalesce(p.full_name, '')
                 || CASE WHEN h.housenum IS NOT NULL AND h.housenum <> ''
                         THEN ', д ' || h.housenum ELSE '' END,
               coalesce(CASE WHEN pv.region_code ~ '^\d+$' THEN pv.region_code::integer END, p.region_code),
               coalesce(pv.postal_code, p.postal_code),
               coalesce(pv.okato, p.okato),
               coalesce(pv.oktmo, p.oktmo),
               coalesce(pv.ifns_ul, p.ifns_ul),
               coalesce(pv.ifns_fl, p.ifns_fl),
               coalesce(pv.kladr_code, p.kladr_code),
               p.region, p.area, p.city, p.settlement, p.street
          FROM hs h
          JOIN ah ON ah.objectid = h.objectid
          LEFT JOIN search.address_objects p ON p.object_id = ah.parentobjid
          LEFT JOIN pv ON pv.objectid = h.objectid;
        """;
}
