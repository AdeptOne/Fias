using System.Xml;
using Fias.Service.Updater.Options;
using Fias.Service.Updater.Services.Archives;
using Fias.Service.Updater.Services.Db;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Fias.Service.Updater.Services.Importing;

public class RoomImporter(
    INpgsqlConnectionFactory factory,
    IOptions<FiasOptions> options,
    ILogger<RoomImporter> logger)
    : CopyImporterBase(factory, options, logger)
{
    public override FiasEntityKind Kind => FiasEntityKind.Rooms;
    protected override string TableName => "fias.rooms";
    protected override string RecordElementName => "ROOM";

    protected override IReadOnlyList<string> Columns =>
    [
        "id", "objectid", "objectguid", "number", "roomtype",
        "opertypeid", "previd", "nextid",
        "updatedate", "startdate", "enddate", "isactual", "isactive"
    ];

    protected override async Task WriteRowAsync(NpgsqlBinaryImporter w, XmlReader r, CancellationToken ct)
    {
        await w.WriteAsync(XmlHelpers.GetLong(r, "ID") ?? 0L, NpgsqlDbType.Bigint, ct);
        await w.WriteAsync(XmlHelpers.GetLong(r, "OBJECTID") ?? 0L, NpgsqlDbType.Bigint, ct);
        await WriteNullable(w, XmlHelpers.GetGuid(r, "OBJECTGUID"), NpgsqlDbType.Uuid, ct);
        await WriteNullable(w, XmlHelpers.GetString(r, "NUMBER"), NpgsqlDbType.Text, ct);
        await WriteNullable(w, XmlHelpers.GetInt(r, "ROOMTYPE"), NpgsqlDbType.Integer, ct);
        await WriteNullable(w, XmlHelpers.GetInt(r, "OPERTYPEID"), NpgsqlDbType.Integer, ct);
        await WriteNullable(w, XmlHelpers.GetLong(r, "PREVID"), NpgsqlDbType.Bigint, ct);
        await WriteNullable(w, XmlHelpers.GetLong(r, "NEXTID"), NpgsqlDbType.Bigint, ct);
        await WriteNullable(w, XmlHelpers.GetDate(r, "UPDATEDATE"), NpgsqlDbType.Date, ct);
        await WriteNullable(w, XmlHelpers.GetDate(r, "STARTDATE"), NpgsqlDbType.Date, ct);
        await WriteNullable(w, XmlHelpers.GetDate(r, "ENDDATE"), NpgsqlDbType.Date, ct);
        await WriteNullable(w, XmlHelpers.GetBool(r, "ISACTUAL"), NpgsqlDbType.Boolean, ct);
        await WriteNullable(w, XmlHelpers.GetBool(r, "ISACTIVE"), NpgsqlDbType.Boolean, ct);
    }

    protected override string BuildUpsertFromStagingSql(string staging) => $"""
        INSERT INTO {TableName} AS t
            (id, objectid, objectguid, number, roomtype,
             opertypeid, previd, nextid,
             updatedate, startdate, enddate, isactual, isactive)
        SELECT id, objectid, objectguid, number, roomtype,
               opertypeid, previd, nextid,
               updatedate, startdate, enddate, isactual, isactive
          FROM {staging}
        ON CONFLICT (id) DO UPDATE SET
            objectid   = excluded.objectid,
            objectguid = excluded.objectguid,
            number     = excluded.number,
            roomtype   = excluded.roomtype,
            opertypeid = excluded.opertypeid,
            previd     = excluded.previd,
            nextid     = excluded.nextid,
            updatedate = excluded.updatedate,
            startdate  = excluded.startdate,
            enddate    = excluded.enddate,
            isactual   = excluded.isactual,
            isactive   = excluded.isactive;
        """;
}
