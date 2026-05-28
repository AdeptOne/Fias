using System.Xml;
using Fias.Service.Updater.Options;
using Fias.Service.Updater.Services.Archives;
using Fias.Service.Updater.Services.Db;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Fias.Service.Updater.Services.Importing;

public class ObjectLevelImporter(
    INpgsqlConnectionFactory factory,
    IOptions<FiasOptions> options,
    ILogger<ObjectLevelImporter> logger)
    : CopyImporterBase(factory, options, logger)
{
    public override FiasEntityKind Kind => FiasEntityKind.ObjectLevels;
    protected override string TableName => "fias.object_levels";
    protected override string RecordElementName => "OBJECTLEVEL";

    protected override IReadOnlyList<string> Columns =>
        ["level", "name", "shortname", "startdate", "enddate", "updatedate", "isactive"];

    protected override async Task WriteRowAsync(NpgsqlBinaryImporter w, XmlReader r, CancellationToken ct)
    {
        await w.WriteAsync(XmlHelpers.GetInt(r, "LEVEL") ?? 0, NpgsqlDbType.Integer, ct);
        await WriteNullable(w, XmlHelpers.GetString(r, "NAME"), NpgsqlDbType.Text, ct);
        await WriteNullable(w, XmlHelpers.GetString(r, "SHORTNAME"), NpgsqlDbType.Text, ct);
        await WriteNullable(w, XmlHelpers.GetDate(r, "STARTDATE"), NpgsqlDbType.Date, ct);
        await WriteNullable(w, XmlHelpers.GetDate(r, "ENDDATE"), NpgsqlDbType.Date, ct);
        await WriteNullable(w, XmlHelpers.GetDate(r, "UPDATEDATE"), NpgsqlDbType.Date, ct);
        await WriteNullable(w, XmlHelpers.GetShort(r, "ISACTIVE"), NpgsqlDbType.Smallint, ct);
    }

    protected override string BuildUpsertFromStagingSql(string staging) => $"""
        INSERT INTO {TableName} AS t
            (level, name, shortname, startdate, enddate, updatedate, isactive)
        SELECT level, name, shortname, startdate, enddate, updatedate, isactive
          FROM {staging}
        ON CONFLICT (level) DO UPDATE SET
            name       = excluded.name,
            shortname  = excluded.shortname,
            startdate  = excluded.startdate,
            enddate    = excluded.enddate,
            updatedate = excluded.updatedate,
            isactive   = excluded.isactive;
        """;
}
