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
/// Zero-downtime: всё строится в теневых таблицах search.*_stage (живые таблицы при этом
/// продолжают обслуживать поиск), индексы строятся по наполненным staging-таблицам (без
/// поддержки GiST/GIN на каждой вставке), и в КОНЦЕ — атомарный swap в одной транзакции
/// (DDL в Postgres транзакционный: при сбое живые таблицы остаются нетронутыми). Лок берётся
/// лишь на короткий момент swap'а (drop+rename — операции уровня метаданных).
///
/// Порядок фаз важен: дома берут полный путь/реквизиты-фолбэк у уже наполненной address_objects_stage.
/// В v1 — полная пересборка (и после full, и после delta).
/// </summary>
public class SearchProjectionBuilder(
    INpgsqlConnectionFactory factory,
    ILogger<SearchProjectionBuilder> logger) : ISearchProjectionBuilder
{
    public async Task RebuildAsync(IProgressSink progress, CancellationToken ct)
    {
        logger.LogInformation("Пересборка денормализованного поискового слоя search.* (staging + swap)");
        var bar = progress.StartProgressBar("Пересборка search.*");

        await using var conn = await factory.OpenAsync(ct);

        await ExecAsync(conn, CreateStageSql, ct);
        progress.WriteLine("search.*: staging-таблицы созданы");
        bar.SetValue(5);

        var types = await ExecAsync(conn, TypesSql, ct);
        progress.WriteLine($"address_object_types: {types} строк");
        bar.SetValue(10);

        var objects = await ExecAsync(conn, AddressObjectsSql, ct);
        progress.WriteLine($"address_objects: {objects} строк");
        bar.SetValue(55);

        var houses = await ExecAsync(conn, HousesSql, ct);
        progress.WriteLine($"houses: {houses} строк");
        bar.SetValue(78);

        await ExecAsync(conn, CreateStageIndexesSql, ct);
        progress.WriteLine("search.*_stage: индексы построены");
        bar.SetValue(92);

        await ExecAsync(conn, AnalyzeStageSql, ct);
        bar.SetValue(95);

        // Атомарный swap: одна транзакция, DDL транзакционный — либо новые данные целиком, либо старые.
        await using (var tx = await conn.BeginTransactionAsync(ct))
        {
            await using var cmd = new NpgsqlCommand(SwapSql, conn, tx) { CommandTimeout = 0 };
            await cmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }
        bar.SetValue(100);
        progress.WriteLine("search.*: атомарный swap выполнен");

        logger.LogInformation(
            "Поисковый слой search.* пересобран (объектов: {Objects}, домов: {Houses})", objects, houses);
    }

    /// <summary>Выполнить фазу. CommandTimeout=0 — фазы по всей базе ГАР длительные.</summary>
    private static async Task<int> ExecAsync(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = 0 };
        return await cmd.ExecuteNonQueryAsync(ct);
    }

    // Теневые таблицы: структура копируется с живых (LIKE), без индексов/PK (их строим после
    // загрузки). INCLUDING GENERATED переносит вычисляемую name_tsv. Остатки от прошлого сбоя сносим.
    private const string CreateStageSql = """
        DROP TABLE IF EXISTS search.address_object_types_stage, search.address_objects_stage, search.houses_stage CASCADE;
        CREATE TABLE search.address_object_types_stage (LIKE search.address_object_types INCLUDING GENERATED INCLUDING DEFAULTS);
        CREATE TABLE search.address_objects_stage      (LIKE search.address_objects      INCLUDING GENERATED INCLUDING DEFAULTS);
        CREATE TABLE search.houses_stage               (LIKE search.houses               INCLUDING GENERATED INCLUDING DEFAULTS);
        """;

    // Индексы и PK строим по уже наполненным staging-таблицам (имена с суффиксом _stage —
    // при swap переименуем в канонические).
    private const string CreateStageIndexesSql = """
        ALTER TABLE search.address_object_types_stage ADD CONSTRAINT address_object_types_stage_pkey PRIMARY KEY (id);
        ALTER TABLE search.address_objects_stage      ADD CONSTRAINT address_objects_stage_pkey      PRIMARY KEY (object_id);
        ALTER TABLE search.houses_stage               ADD CONSTRAINT houses_stage_pkey               PRIMARY KEY (object_id);
        -- GIN по tsvector — точный/морфологический FTS (@@).
        CREATE INDEX ix_search_ao_name_tsv_stage  ON search.address_objects_stage USING gin (name_tsv);
        -- GiST по триграммам (нечёткий поиск + KNN name <-> :q).
        CREATE INDEX ix_search_ao_name_trgm_stage ON search.address_objects_stage USING gist (name gist_trgm_ops);
        CREATE INDEX ix_search_ao_parent_stage    ON search.address_objects_stage (parent_object_id);
        CREATE INDEX ix_search_ao_region_stage    ON search.address_objects_stage (region_object_id);
        CREATE INDEX ix_search_ao_guid_stage      ON search.address_objects_stage (object_guid);
        CREATE INDEX ix_search_ao_path_stage      ON search.address_objects_stage (path text_pattern_ops);
        CREATE INDEX ix_search_houses_parent_stage   ON search.houses_stage (parent_object_id);
        CREATE INDEX ix_search_houses_path_stage     ON search.houses_stage (path text_pattern_ops);
        CREATE INDEX ix_search_houses_num_trgm_stage ON search.houses_stage USING gin (house_num gin_trgm_ops);
        CREATE INDEX ix_search_houses_guid_stage     ON search.houses_stage (object_guid);
        """;

    private const string AnalyzeStageSql = """
        ANALYZE search.address_object_types_stage;
        ANALYZE search.address_objects_stage;
        ANALYZE search.houses_stage;
        """;

    // Атомарная замена: сносим живые, переименовываем staging -> канон, индексы/PK -> канон.
    // Выполняется в одной транзакции (см. RebuildAsync) — Postgres-DDL транзакционен.
    private const string SwapSql = """
        DROP TABLE IF EXISTS search.address_object_types, search.address_objects, search.houses CASCADE;

        ALTER TABLE search.address_object_types_stage RENAME TO address_object_types;
        ALTER TABLE search.address_objects_stage      RENAME TO address_objects;
        ALTER TABLE search.houses_stage               RENAME TO houses;

        ALTER TABLE search.address_object_types RENAME CONSTRAINT address_object_types_stage_pkey TO address_object_types_pkey;
        ALTER TABLE search.address_objects      RENAME CONSTRAINT address_objects_stage_pkey      TO address_objects_pkey;
        ALTER TABLE search.houses               RENAME CONSTRAINT houses_stage_pkey               TO houses_pkey;

        ALTER INDEX search.ix_search_ao_name_tsv_stage     RENAME TO ix_search_ao_name_tsv;
        ALTER INDEX search.ix_search_ao_name_trgm_stage    RENAME TO ix_search_ao_name_trgm;
        ALTER INDEX search.ix_search_ao_parent_stage       RENAME TO ix_search_ao_parent;
        ALTER INDEX search.ix_search_ao_region_stage       RENAME TO ix_search_ao_region;
        ALTER INDEX search.ix_search_ao_guid_stage         RENAME TO ix_search_ao_guid;
        ALTER INDEX search.ix_search_ao_path_stage         RENAME TO ix_search_ao_path;
        ALTER INDEX search.ix_search_houses_parent_stage   RENAME TO ix_search_houses_parent;
        ALTER INDEX search.ix_search_houses_path_stage     RENAME TO ix_search_houses_path;
        ALTER INDEX search.ix_search_houses_num_trgm_stage RENAME TO ix_search_houses_num_trgm;
        ALTER INDEX search.ix_search_houses_guid_stage     RENAME TO ix_search_houses_guid;
        """;

    // --- Фаза «типы» ---------------------------------------------------------
    private const string TypesSql = """
        INSERT INTO search.address_object_types_stage (id, level, short_name, name)
        SELECT id, level, shortname, name
          FROM fias.addressobject_types;
        """;

    // --- Фаза «адресообразующие объекты»: полный путь + структурный сплит + реквизиты --------
    //     DISTINCT ON (objectid) — страховка от исторических дублей (PK проекции — object_id).
    private const string AddressObjectsSql = """
        INSERT INTO search.address_objects_stage
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
        INSERT INTO search.houses_stage
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
          LEFT JOIN search.address_objects_stage p ON p.object_id = ah.parentobjid
          LEFT JOIN pv ON pv.objectid = h.objectid;
        """;
}
