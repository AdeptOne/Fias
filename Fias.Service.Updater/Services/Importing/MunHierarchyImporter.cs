using System.Xml;
using Fias.Service.Updater.Options;
using Fias.Service.Updater.Services.Archives;
using Fias.Service.Updater.Services.Db;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Fias.Service.Updater.Services.Importing;

public class MunHierarchyImporter(
    INpgsqlConnectionFactory factory,
    IOptions<FiasOptions> options,
    ILogger<MunHierarchyImporter> logger)
    : CopyImporterBase(factory, options, logger)
{
    public override FiasEntityKind Kind => FiasEntityKind.MunHierarchy;
    protected override string TableName => "fias.mun_hierarchy";
    protected override string RecordElementName => "ITEM";

    protected override IReadOnlyList<string> Columns =>
    [
        "id", "objectid", "parentobjid", "path",
        "updatedate", "startdate", "enddate", "isactive"
    ];

    protected override async Task WriteRowAsync(NpgsqlBinaryImporter w, XmlReader r, CancellationToken ct)
    {
        await w.WriteAsync(XmlHelpers.GetLong(r, "ID") ?? 0L, NpgsqlDbType.Bigint, ct);
        await w.WriteAsync(XmlHelpers.GetLong(r, "OBJECTID") ?? 0L, NpgsqlDbType.Bigint, ct);
        await WriteNullable(w, XmlHelpers.GetLong(r, "PARENTOBJID"), NpgsqlDbType.Bigint, ct);
        await WriteNullable(w, XmlHelpers.GetString(r, "PATH"), NpgsqlDbType.Text, ct);
        await WriteNullable(w, XmlHelpers.GetDate(r, "UPDATEDATE"), NpgsqlDbType.Date, ct);
        await WriteNullable(w, XmlHelpers.GetDate(r, "STARTDATE"), NpgsqlDbType.Date, ct);
        await WriteNullable(w, XmlHelpers.GetDate(r, "ENDDATE"), NpgsqlDbType.Date, ct);
        await WriteNullable(w, XmlHelpers.GetBool(r, "ISACTIVE"), NpgsqlDbType.Boolean, ct);
    }

    protected override string BuildUpsertFromStagingSql(string staging) => $"""
        INSERT INTO {TableName} AS t
            (id, objectid, parentobjid, path, updatedate, startdate, enddate, isactive)
        SELECT id, objectid, parentobjid, path, updatedate, startdate, enddate, isactive
          FROM {staging}
        ON CONFLICT (id) DO UPDATE SET
            objectid    = excluded.objectid,
            parentobjid = excluded.parentobjid,
            path        = excluded.path,
            updatedate  = excluded.updatedate,
            startdate   = excluded.startdate,
            enddate     = excluded.enddate,
            isactive    = excluded.isactive;
        """;
}
