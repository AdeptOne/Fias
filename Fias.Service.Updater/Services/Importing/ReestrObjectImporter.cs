using System.Xml;
using Fias.Service.Updater.Options;
using Fias.Service.Updater.Services.Archives;
using Fias.Service.Updater.Services.Db;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Fias.Service.Updater.Services.Importing;

public class ReestrObjectImporter(
    INpgsqlConnectionFactory factory,
    IOptions<FiasOptions> options,
    ILogger<ReestrObjectImporter> logger)
    : CopyImporterBase(factory, options, logger)
{
    public override FiasEntityKind Kind => FiasEntityKind.ReestrObjects;
    protected override string TableName => "fias.reestr_objects";
    protected override string RecordElementName => "OBJECT";

    protected override IReadOnlyList<string> Columns =>
    [
        "objectid", "objectguid", "changeid", "levelid",
        "updatedate", "createdate", "isactive"
    ];

    protected override async Task WriteRowAsync(NpgsqlBinaryImporter w, XmlReader r, CancellationToken ct)
    {
        await w.WriteAsync(XmlHelpers.GetLong(r, "OBJECTID") ?? 0L, NpgsqlDbType.Bigint, ct);
        await WriteNullable(w, XmlHelpers.GetGuid(r, "OBJECTGUID"), NpgsqlDbType.Uuid, ct);
        await WriteNullable(w, XmlHelpers.GetLong(r, "CHANGEID"), NpgsqlDbType.Bigint, ct);
        await WriteNullable(w, XmlHelpers.GetInt(r, "LEVELID"), NpgsqlDbType.Integer, ct);
        await WriteNullable(w, XmlHelpers.GetDate(r, "UPDATEDATE"), NpgsqlDbType.Date, ct);
        await WriteNullable(w, XmlHelpers.GetDate(r, "CREATEDATE"), NpgsqlDbType.Date, ct);
        await WriteNullable(w, XmlHelpers.GetShort(r, "ISACTIVE"), NpgsqlDbType.Smallint, ct);
    }

    protected override string BuildUpsertFromStagingSql(string staging) => $"""
        INSERT INTO {TableName} AS t
            (objectid, objectguid, changeid, levelid, updatedate, createdate, isactive)
        SELECT objectid, objectguid, changeid, levelid, updatedate, createdate, isactive
          FROM {staging}
        ON CONFLICT (objectid) DO UPDATE SET
            objectguid = excluded.objectguid,
            changeid   = excluded.changeid,
            levelid    = excluded.levelid,
            updatedate = excluded.updatedate,
            createdate = excluded.createdate,
            isactive   = excluded.isactive;
        """;
}
