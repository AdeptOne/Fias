using System.Xml;
using Fias.Service.Updater.Options;
using Fias.Service.Updater.Services.Archives;
using Fias.Service.Updater.Services.Db;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;
using static Fias.Service.Updater.Services.Importing.AddressObjectImporter;

namespace Fias.Service.Updater.Services.Importing;

public class AddressObjectTypeImporter(
    INpgsqlConnectionFactory factory,
    IOptions<FiasOptions> options,
    ILogger<AddressObjectTypeImporter> logger)
    : CopyImporterBase(factory, options, logger)
{
    public override FiasEntityKind Kind => FiasEntityKind.AddressObjectTypes;

    protected override string TableName => "fias.addressobject_types";

    protected override string RecordElementName => "ADDRESSOBJECTTYPE";

    protected override IReadOnlyList<string> Columns =>
    [
        "id", "level", "shortname", "name",
        "startdate", "enddate", "updatedate", "isactive"
    ];

    protected override async Task WriteRowAsync(NpgsqlBinaryImporter w, XmlReader r, CancellationToken ct)
    {
        await w.WriteAsync(XmlHelpers.GetInt(r, "ID") ?? 0, NpgsqlDbType.Integer, ct);
        await WriteNullable(w, XmlHelpers.GetInt(r, "LEVEL"), NpgsqlDbType.Integer, ct);
        await WriteNullable(w, XmlHelpers.GetString(r, "SHORTNAME"), NpgsqlDbType.Text, ct);
        await WriteNullable(w, XmlHelpers.GetString(r, "NAME"), NpgsqlDbType.Text, ct);
        await WriteNullable(w, XmlHelpers.GetDate(r, "STARTDATE"), NpgsqlDbType.Date, ct);
        await WriteNullable(w, XmlHelpers.GetDate(r, "ENDDATE"), NpgsqlDbType.Date, ct);
        await WriteNullable(w, XmlHelpers.GetDate(r, "UPDATEDATE"), NpgsqlDbType.Date, ct);
        await WriteNullable(w, XmlHelpers.GetShort(r, "ISACTIVE"), NpgsqlDbType.Smallint, ct);
    }

    protected override string BuildUpsertFromStagingSql(string staging) => $"""
        INSERT INTO {TableName} AS t
            (id, level, shortname, name, startdate, enddate, updatedate, isactive)
        SELECT id, level, shortname, name, startdate, enddate, updatedate, isactive
          FROM {staging}
        ON CONFLICT (id) DO UPDATE SET
            level      = excluded.level,
            shortname  = excluded.shortname,
            name       = excluded.name,
            startdate  = excluded.startdate,
            enddate    = excluded.enddate,
            updatedate = excluded.updatedate,
            isactive   = excluded.isactive;
        """;
}
