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
        var sql = "TRUNCATE TABLE " + string.Join(", ", DataTables) + " RESTART IDENTITY";
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
            isactive    smallint
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
            isactual    smallint,
            isactive    smallint
        );
        CREATE INDEX IF NOT EXISTS ix_addressobjects_objectid ON fias.addressobjects(objectid);
        CREATE INDEX IF NOT EXISTS ix_addressobjects_name_trgm
            ON fias.addressobjects USING gin (name gin_trgm_ops)
            WHERE isactive = 1 AND isactual = 1;
        CREATE INDEX IF NOT EXISTS ix_addressobjects_objectguid ON fias.addressobjects(objectguid)
            WHERE isactive = 1 AND isactual = 1;

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
            isactual    smallint,
            isactive    smallint
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
            isactual    smallint,
            isactive    smallint
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
            isactual    smallint,
            isactive    smallint
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
            isactive    smallint
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
            isactive    smallint
        );
        CREATE INDEX IF NOT EXISTS ix_adm_hierarchy_objectid ON fias.adm_hierarchy(objectid);
        CREATE INDEX IF NOT EXISTS ix_adm_hierarchy_parentobjid ON fias.adm_hierarchy(parentobjid)
            WHERE isactive = 1;

        CREATE TABLE IF NOT EXISTS fias.addressobject_types (
            id          integer PRIMARY KEY,
            level       integer,
            shortname   text,
            name        text,
            startdate   date,
            enddate     date,
            updatedate  date,
            isactive    smallint
        );

        CREATE TABLE IF NOT EXISTS fias.house_types (
            id          integer PRIMARY KEY,
            shortname   text,
            name        text,
            startdate   date,
            enddate     date,
            updatedate  date,
            isactive    smallint
        );

        CREATE TABLE IF NOT EXISTS fias.apartment_types (
            id          integer PRIMARY KEY,
            shortname   text,
            name        text,
            startdate   date,
            enddate     date,
            updatedate  date,
            isactive    smallint
        );

        CREATE TABLE IF NOT EXISTS fias.room_types (
            id          integer PRIMARY KEY,
            shortname   text,
            name        text,
            startdate   date,
            enddate     date,
            updatedate  date,
            isactive    smallint
        );

        CREATE TABLE IF NOT EXISTS fias.object_levels (
            level       integer PRIMARY KEY,
            name        text,
            shortname   text,
            startdate   date,
            enddate     date,
            updatedate  date,
            isactive    smallint
        );

        CREATE TABLE IF NOT EXISTS fias.params (
            id          bigint PRIMARY KEY,
            objectid    bigint NOT NULL,
            changeid    bigint,
            changeidend bigint,
            typeid      integer,
            value       text,
            updatedate  date,
            startdate   date,
            enddate     date
        );
        CREATE INDEX IF NOT EXISTS ix_params_objectid_typeid ON fias.params(objectid, typeid);
        """;
}
