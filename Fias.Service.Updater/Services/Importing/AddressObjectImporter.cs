using System.Xml;
using Fias.Service.Updater.Options;
using Fias.Service.Updater.Services.Archives;
using Fias.Service.Updater.Services.Db;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Fias.Service.Updater.Services.Importing;

public class AddressObjectImporter(
    INpgsqlConnectionFactory factory,
    IOptions<FiasOptions> options,
    ILogger<AddressObjectImporter> logger)
    : CopyImporterBase(factory, options, logger)
{
    public override FiasEntityKind Kind => FiasEntityKind.AddressObjects;

    protected override string TableName => "fias.addressobjects";

    protected override string RecordElementName => "OBJECT";

    protected override IReadOnlyList<string> Columns =>
    [
        "id", "objectid", "objectguid", "name", "typename", "level",
        "opertypeid", "previd", "nextid",
        "updatedate", "startdate", "enddate",
        "isactual", "isactive"
    ];

    protected override async Task WriteRowAsync(NpgsqlBinaryImporter w, XmlReader r, CancellationToken ct)
    {
        await w.WriteAsync(XmlHelpers.GetLong(r, "ID") ?? 0L, NpgsqlDbType.Bigint, ct);
        await w.WriteAsync(XmlHelpers.GetLong(r, "OBJECTID") ?? 0L, NpgsqlDbType.Bigint, ct);
        await WriteNullable(w, XmlHelpers.GetGuid(r, "OBJECTGUID"), NpgsqlDbType.Uuid, ct);
        await WriteNullable(w, XmlHelpers.GetString(r, "NAME"), NpgsqlDbType.Text, ct);
        await WriteNullable(w, XmlHelpers.GetString(r, "TYPENAME"), NpgsqlDbType.Text, ct);
        await WriteNullable(w, XmlHelpers.GetInt(r, "LEVEL"), NpgsqlDbType.Integer, ct);
        await WriteNullable(w, XmlHelpers.GetInt(r, "OPERTYPEID"), NpgsqlDbType.Integer, ct);
        await WriteNullable(w, XmlHelpers.GetLong(r, "PREVID"), NpgsqlDbType.Bigint, ct);
        await WriteNullable(w, XmlHelpers.GetLong(r, "NEXTID"), NpgsqlDbType.Bigint, ct);
        await WriteNullable(w, XmlHelpers.GetDate(r, "UPDATEDATE"), NpgsqlDbType.Date, ct);
        await WriteNullable(w, XmlHelpers.GetDate(r, "STARTDATE"), NpgsqlDbType.Date, ct);
        await WriteNullable(w, XmlHelpers.GetDate(r, "ENDDATE"), NpgsqlDbType.Date, ct);
        await WriteNullable(w, XmlHelpers.GetShort(r, "ISACTUAL"), NpgsqlDbType.Smallint, ct);
        await WriteNullable(w, XmlHelpers.GetShort(r, "ISACTIVE"), NpgsqlDbType.Smallint, ct);
    }

    protected override string BuildUpsertFromStagingSql(string staging) => $"""
        INSERT INTO {TableName} AS t
            (id, objectid, objectguid, name, typename, level, opertypeid, previd, nextid,
             updatedate, startdate, enddate, isactual, isactive)
        SELECT id, objectid, objectguid, name, typename, level, opertypeid, previd, nextid,
               updatedate, startdate, enddate, isactual, isactive
          FROM {staging}
        ON CONFLICT (id) DO UPDATE SET
            objectid    = excluded.objectid,
            objectguid  = excluded.objectguid,
            name        = excluded.name,
            typename    = excluded.typename,
            level       = excluded.level,
            opertypeid  = excluded.opertypeid,
            previd      = excluded.previd,
            nextid      = excluded.nextid,
            updatedate  = excluded.updatedate,
            startdate   = excluded.startdate,
            enddate     = excluded.enddate,
            isactual    = excluded.isactual,
            isactive    = excluded.isactive;
        """;

    internal static async Task WriteNullable<T>(NpgsqlBinaryImporter w, T? value, NpgsqlDbType type, CancellationToken ct)
        where T : struct
    {
        if (value.HasValue) await w.WriteAsync(value.Value, type, ct);
        else await w.WriteNullAsync(ct);
    }

    internal static async Task WriteNullable(NpgsqlBinaryImporter w, string? value, NpgsqlDbType type, CancellationToken ct)
    {
        if (value is not null) await w.WriteAsync(value, type, ct);
        else await w.WriteNullAsync(ct);
    }
}
