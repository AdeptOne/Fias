using System.Xml;
using Fias.Service.Updater.Options;
using Fias.Service.Updater.Services.Archives;
using Fias.Service.Updater.Services.Db;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Fias.Service.Updater.Services.Importing;

public class ParamImporter(
    INpgsqlConnectionFactory factory,
    IOptions<FiasOptions> options,
    ILogger<ParamImporter> logger)
    : CopyImporterBase(factory, options, logger)
{
    public override FiasEntityKind Kind => FiasEntityKind.Param;
    protected override string TableName => "fias.params";
    protected override string RecordElementName => "PARAM";

    protected override IReadOnlyList<string> Columns =>
    [
        "id", "objectid", "changeid", "changeidend", "typeid",
        "value", "updatedate", "startdate", "enddate"
    ];

    protected override async Task WriteRowAsync(NpgsqlBinaryImporter w, XmlReader r, CancellationToken ct)
    {
        await w.WriteAsync(XmlHelpers.GetLong(r, "ID") ?? 0L, NpgsqlDbType.Bigint, ct);
        await w.WriteAsync(XmlHelpers.GetLong(r, "OBJECTID") ?? 0L, NpgsqlDbType.Bigint, ct);
        await WriteNullable(w, XmlHelpers.GetLong(r, "CHANGEID"), NpgsqlDbType.Bigint, ct);
        await WriteNullable(w, XmlHelpers.GetLong(r, "CHANGEIDEND"), NpgsqlDbType.Bigint, ct);
        await WriteNullable(w, XmlHelpers.GetInt(r, "TYPEID"), NpgsqlDbType.Integer, ct);
        await WriteNullable(w, XmlHelpers.GetString(r, "VALUE"), NpgsqlDbType.Text, ct);
        await WriteNullable(w, XmlHelpers.GetDate(r, "UPDATEDATE"), NpgsqlDbType.Date, ct);
        await WriteNullable(w, XmlHelpers.GetDate(r, "STARTDATE"), NpgsqlDbType.Date, ct);
        await WriteNullable(w, XmlHelpers.GetDate(r, "ENDDATE"), NpgsqlDbType.Date, ct);
    }

    protected override string BuildUpsertFromStagingSql(string staging) => $"""
        INSERT INTO {TableName} AS t
            (id, objectid, changeid, changeidend, typeid, value,
             updatedate, startdate, enddate)
        SELECT id, objectid, changeid, changeidend, typeid, value,
               updatedate, startdate, enddate
          FROM {staging}
        ON CONFLICT (id) DO UPDATE SET
            objectid    = excluded.objectid,
            changeid    = excluded.changeid,
            changeidend = excluded.changeidend,
            typeid      = excluded.typeid,
            value       = excluded.value,
            updatedate  = excluded.updatedate,
            startdate   = excluded.startdate,
            enddate     = excluded.enddate;
        """;
}
