using System.Xml;
using Fias.Service.Updater.Options;
using Fias.Service.Updater.Services.Archives;
using Fias.Service.Updater.Services.Db;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Fias.Service.Updater.Services.Importing;

/// <summary>
/// Базовый импортёр параметров (PARAM). В ГАР параметры разбиты по семействам объектов
/// (addr_obj/houses/apartments/rooms/steads/carplaces), и XML-ID уникален лишь ВНУТРИ семейства —
/// между семействами ID пересекаются. Поэтому в fias.params ключ составной (objtype, id),
/// а каждый конкретный импортёр проставляет свой <see cref="ObjType"/>.
/// </summary>
public abstract class ParamImporterBase(
    INpgsqlConnectionFactory factory,
    IOptions<FiasOptions> options,
    ILogger logger)
    : CopyImporterBase(factory, options, logger)
{
    /// <summary>Дискриминатор семейства (objtype) — стабильный код, попадает в ключ.</summary>
    protected abstract short ObjType { get; }

    protected override string TableName => "fias.params";
    protected override string RecordElementName => "PARAM";

    protected override IReadOnlyList<string> Columns =>
    [
        "objtype", "id", "objectid", "changeid", "changeidend", "typeid",
        "value", "updatedate", "startdate", "enddate"
    ];

    protected override async Task WriteRowAsync(NpgsqlBinaryImporter w, XmlReader r, CancellationToken ct)
    {
        await w.WriteAsync(ObjType, NpgsqlDbType.Smallint, ct);
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
            (objtype, id, objectid, changeid, changeidend, typeid, value,
             updatedate, startdate, enddate)
        SELECT objtype, id, objectid, changeid, changeidend, typeid, value,
               updatedate, startdate, enddate
          FROM {staging}
        ON CONFLICT (objtype, id) DO UPDATE SET
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

public sealed class AddrObjParamImporter(
    INpgsqlConnectionFactory factory, IOptions<FiasOptions> options, ILogger<AddrObjParamImporter> logger)
    : ParamImporterBase(factory, options, logger)
{
    public override FiasEntityKind Kind => FiasEntityKind.ParamAddrObj;
    protected override short ObjType => 1;
}

public sealed class HousesParamImporter(
    INpgsqlConnectionFactory factory, IOptions<FiasOptions> options, ILogger<HousesParamImporter> logger)
    : ParamImporterBase(factory, options, logger)
{
    public override FiasEntityKind Kind => FiasEntityKind.ParamHouses;
    protected override short ObjType => 2;
}

public sealed class ApartmentsParamImporter(
    INpgsqlConnectionFactory factory, IOptions<FiasOptions> options, ILogger<ApartmentsParamImporter> logger)
    : ParamImporterBase(factory, options, logger)
{
    public override FiasEntityKind Kind => FiasEntityKind.ParamApartments;
    protected override short ObjType => 3;
}

public sealed class RoomsParamImporter(
    INpgsqlConnectionFactory factory, IOptions<FiasOptions> options, ILogger<RoomsParamImporter> logger)
    : ParamImporterBase(factory, options, logger)
{
    public override FiasEntityKind Kind => FiasEntityKind.ParamRooms;
    protected override short ObjType => 4;
}

public sealed class SteadsParamImporter(
    INpgsqlConnectionFactory factory, IOptions<FiasOptions> options, ILogger<SteadsParamImporter> logger)
    : ParamImporterBase(factory, options, logger)
{
    public override FiasEntityKind Kind => FiasEntityKind.ParamSteads;
    protected override short ObjType => 5;
}

public sealed class CarplacesParamImporter(
    INpgsqlConnectionFactory factory, IOptions<FiasOptions> options, ILogger<CarplacesParamImporter> logger)
    : ParamImporterBase(factory, options, logger)
{
    public override FiasEntityKind Kind => FiasEntityKind.ParamCarplaces;
    protected override short ObjType => 6;
}
