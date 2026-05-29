using Fias.Service.Updater.Services.Db;
using Npgsql;

namespace Fias.Service.Updater.Services.Schema;

public interface IMigrator
{
    Task EnsureSchemaAsync(CancellationToken ct);
    Task TruncateAllAsync(CancellationToken ct);
}

public class Migrator(INpgsqlConnectionFactory factory, ILogger<Migrator> logger) : IMigrator
{
    private static readonly string[] DataTables =
    [
        "fias.reestr_objects",
        "fias.addressobjects",
        "fias.houses",
        "fias.apartments",
        "fias.rooms",
        "fias.mun_hierarchy",
        "fias.adm_hierarchy",
        "fias.addressobject_types",
        "fias.house_types",
        "fias.apartment_types",
        "fias.room_types",
        "fias.object_levels",
        "fias.params"
    ];

    public async Task EnsureSchemaAsync(CancellationToken ct)
    {
        logger.LogInformation("Применяем схему fias.*");

        await using var conn = await factory.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(Ddl, conn);
        await cmd.ExecuteNonQueryAsync(ct);

        logger.LogInformation("Схема актуальна");
    }

    public async Task TruncateAllAsync(CancellationToken ct)
    {
        logger.LogWarning("Полная очистка таблиц fias.* перед перезаливкой");

        await using var conn = await factory.OpenAsync(ct);
        // RESTART IDENTITY не нужен — все PK в схеме явные bigint/integer без serial.
        var sql = "TRUNCATE TABLE " + string.Join(", ", DataTables);
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Схема покрывает базовый набор по документу «Правила формирования адресной строки».
    // Имена столбцов оставлены ровно как в XML атрибутах ФИАС (UPPERCASE) — упрощает COPY-импорт.
    // Первичный ключ — суррогатный ID из XML, кроме иерархий, где он тоже уникален.
    private const string Ddl = """
        CREATE SCHEMA IF NOT EXISTS fias;
        CREATE EXTENSION IF NOT EXISTS pg_trgm;

        CREATE TABLE IF NOT EXISTS fias.import_state (
            id              integer PRIMARY KEY DEFAULT 1,
            version_id      integer NOT NULL,
            text_version    text NOT NULL,
            applied_at      timestamptz NOT NULL DEFAULT now(),
            CONSTRAINT import_state_single CHECK (id = 1)
        );

        CREATE TABLE IF NOT EXISTS fias.reestr_objects (
            objectid    bigint PRIMARY KEY,
            objectguid  uuid,
            changeid    bigint,
            levelid     integer,
            updatedate  date,
            createdate  date,
            isactive    boolean
        );

        CREATE TABLE IF NOT EXISTS fias.addressobjects (
            id          bigint PRIMARY KEY,
            objectid    bigint NOT NULL,
            objectguid  uuid,
            name        text,
            typename    text,
            level       integer,
            opertypeid  integer,
            previd      bigint,
            nextid      bigint,
            updatedate  date,
            startdate   date,
            enddate     date,
            isactual    boolean,
            isactive    boolean
        );
        CREATE INDEX IF NOT EXISTS ix_addressobjects_objectid ON fias.addressobjects(objectid);
        CREATE INDEX IF NOT EXISTS ix_addressobjects_name_trgm
            ON fias.addressobjects USING gin (name gin_trgm_ops)
            WHERE isactive = true AND isactual = true;
        CREATE INDEX IF NOT EXISTS ix_addressobjects_objectguid ON fias.addressobjects(objectguid)
            WHERE isactive = true AND isactual = true;

        CREATE TABLE IF NOT EXISTS fias.houses (
            id          bigint PRIMARY KEY,
            objectid    bigint NOT NULL,
            objectguid  uuid,
            housenum    text,
            addnum1     text,
            addnum2     text,
            housetype   integer,
            addtype1    integer,
            addtype2    integer,
            opertypeid  integer,
            previd      bigint,
            nextid      bigint,
            updatedate  date,
            startdate   date,
            enddate     date,
            isactual    boolean,
            isactive    boolean
        );
        CREATE INDEX IF NOT EXISTS ix_houses_objectid ON fias.houses(objectid);

        CREATE TABLE IF NOT EXISTS fias.apartments (
            id          bigint PRIMARY KEY,
            objectid    bigint NOT NULL,
            objectguid  uuid,
            number      text,
            aparttype   integer,
            opertypeid  integer,
            previd      bigint,
            nextid      bigint,
            updatedate  date,
            startdate   date,
            enddate     date,
            isactual    boolean,
            isactive    boolean
        );
        CREATE INDEX IF NOT EXISTS ix_apartments_objectid ON fias.apartments(objectid);

        CREATE TABLE IF NOT EXISTS fias.rooms (
            id          bigint PRIMARY KEY,
            objectid    bigint NOT NULL,
            objectguid  uuid,
            number      text,
            roomtype    integer,
            opertypeid  integer,
            previd      bigint,
            nextid      bigint,
            updatedate  date,
            startdate   date,
            enddate     date,
            isactual    boolean,
            isactive    boolean
        );
        CREATE INDEX IF NOT EXISTS ix_rooms_objectid ON fias.rooms(objectid);

        CREATE TABLE IF NOT EXISTS fias.mun_hierarchy (
            id          bigint PRIMARY KEY,
            objectid    bigint NOT NULL,
            parentobjid bigint,
            path        text,
            updatedate  date,
            startdate   date,
            enddate     date,
            isactive    boolean
        );
        CREATE INDEX IF NOT EXISTS ix_mun_hierarchy_objectid ON fias.mun_hierarchy(objectid);

        CREATE TABLE IF NOT EXISTS fias.adm_hierarchy (
            id          bigint PRIMARY KEY,
            objectid    bigint NOT NULL,
            parentobjid bigint,
            path        text,
            updatedate  date,
            startdate   date,
            enddate     date,
            isactive    boolean
        );
        CREATE INDEX IF NOT EXISTS ix_adm_hierarchy_objectid ON fias.adm_hierarchy(objectid);
        CREATE INDEX IF NOT EXISTS ix_adm_hierarchy_parentobjid ON fias.adm_hierarchy(parentobjid)
            WHERE isactive = true;

        CREATE TABLE IF NOT EXISTS fias.addressobject_types (
            id          integer PRIMARY KEY,
            level       integer,
            shortname   text,
            name        text,
            startdate   date,
            enddate     date,
            updatedate  date,
            isactive    boolean
        );

        CREATE TABLE IF NOT EXISTS fias.house_types (
            id          integer PRIMARY KEY,
            shortname   text,
            name        text,
            startdate   date,
            enddate     date,
            updatedate  date,
            isactive    boolean
        );

        CREATE TABLE IF NOT EXISTS fias.apartment_types (
            id          integer PRIMARY KEY,
            shortname   text,
            name        text,
            startdate   date,
            enddate     date,
            updatedate  date,
            isactive    boolean
        );

        CREATE TABLE IF NOT EXISTS fias.room_types (
            id          integer PRIMARY KEY,
            shortname   text,
            name        text,
            startdate   date,
            enddate     date,
            updatedate  date,
            isactive    boolean
        );

        CREATE TABLE IF NOT EXISTS fias.object_levels (
            level       integer PRIMARY KEY,
            name        text,
            shortname   text,
            startdate   date,
            enddate     date,
            updatedate  date,
            isactive    boolean
        );

        -- В fias.params сваливаются параметры всех семейств (addr_obj/houses/apartments/...),
        -- а их XML-овый ID уникален лишь внутри своего семейства и пересекается между ними.
        -- Поэтому ключ составной: (objtype, id). objtype — дискриминатор семейства.
        -- Идемпотентная разовая миграция: если таблица старой формы (без objtype) — пересоздаём.
        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'fias' AND table_name = 'params' AND column_name = 'objtype')
            THEN
                DROP TABLE IF EXISTS fias.params;
                CREATE TABLE fias.params (
                    objtype     smallint NOT NULL,
                    id          bigint   NOT NULL,
                    objectid    bigint   NOT NULL,
                    changeid    bigint,
                    changeidend bigint,
                    typeid      integer,
                    value       text,
                    updatedate  date,
                    startdate   date,
                    enddate     date,
                    PRIMARY KEY (objtype, id)
                );
                CREATE INDEX ix_params_objectid_typeid ON fias.params(objectid, typeid);
            END IF;
        END $$;

        -- =====================================================================
        --  Денормализованный поисковый слой (schema search). Чистый snake_case.
        --  Плоские таблицы наполняются ПОСЛЕ импорта (SearchProjectionBuilder).
        --  Поиск читает только их — без JOIN по сырым fias.* и без обхода дерева.
        -- =====================================================================
        CREATE SCHEMA IF NOT EXISTS search;

        -- Справочник типов адресообразующих элементов (для отображения/фильтров).
        CREATE TABLE IF NOT EXISTS search.address_object_types (
            id          integer PRIMARY KEY,
            level       integer,
            short_name  text,
            name        text
        );

        -- Регионы/города/улицы: имя + денормализованный полный путь + FTS-вектор.
        CREATE TABLE IF NOT EXISTS search.address_objects (
            object_id         bigint PRIMARY KEY,
            object_guid       uuid,
            parent_object_id  bigint,      -- непосредственный родитель в адм. дереве
            parent_guid       uuid,
            region_object_id  bigint,      -- корень пути (субъект РФ) — для приоритета/фильтра
            path              text,        -- денормализованный путь (objectid.objectid…) для сужения поддерева
            level             integer,
            type_name         text,
            name              text,
            full_name         text,        -- денормализованная полная адресная строка (для отображения)
            -- Реквизиты объекта (из fias.params) — для инлайн-выдачи в стиле DaData.
            region_code       integer,
            postal_code       text,
            okato             text,
            oktmo             text,
            ifns_ul           text,
            ifns_fl           text,
            kladr_code        text,
            -- Структурный сплит адреса (имена предков по уровням ГАР).
            region            text,
            area              text,
            city              text,
            settlement        text,
            street            text,
            name_tsv          tsvector GENERATED ALWAYS AS (to_tsvector('russian', coalesce(name, ''))) STORED
        );

        -- Дома, привязанные к parent_object_id/parent_guid (улица/нас. пункт).
        CREATE TABLE IF NOT EXISTS search.houses (
            object_id         bigint PRIMARY KEY,
            object_guid       uuid,
            parent_object_id  bigint,
            parent_guid       uuid,
            path              text,
            house_num         text,
            add_num1          text,
            add_num2          text,
            house_type        integer,
            add_type1         integer,
            add_type2         integer,
            full_name         text,
            -- Реквизиты дома (из fias.params).
            region_code       integer,
            postal_code       text,
            okato             text,
            oktmo             text,
            ifns_ul           text,
            ifns_fl           text,
            kladr_code        text,
            -- Структурный сплит наследуется от родителя (улицы/нас. пункта); house = house_num.
            region            text,
            area              text,
            city              text,
            settlement        text,
            street            text
        );

        -- Вторичные индексы search.* НЕ создаём здесь: их строит SearchProjectionBuilder
        -- после массовой загрузки (drop → INSERT → create), чтобы не платить за поддержку
        -- GiST/GIN на каждой вставке. Здесь — только таблицы и PK.
        """;
}
