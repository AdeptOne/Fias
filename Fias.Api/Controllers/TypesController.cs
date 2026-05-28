using Fias.Application.Models;
using Fias.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fias.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/types")]
[Produces("application/json")]
public class TypesController(IReferencesService refs) : ControllerBase
{
    /// <summary>Типы адресообразующих элементов; опциональный фильтр по уровню.</summary>
    [HttpGet("address-objects")]
    [ProducesResponseType(typeof(IReadOnlyList<TypeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TypeDto>>> AddressObjects(
        [FromQuery] int? level = null, CancellationToken ct = default)
        => Ok(await refs.GetAddressObjectTypesAsync(level, ct));

    /// <summary>Типы зданий (д., стр., корп.).</summary>
    [HttpGet("houses")]
    [ProducesResponseType(typeof(IReadOnlyList<TypeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TypeDto>>> Houses(CancellationToken ct)
        => Ok(await refs.GetHouseTypesAsync(ct));

    /// <summary>Типы помещений (кв., оф., пом.).</summary>
    [HttpGet("apartments")]
    [ProducesResponseType(typeof(IReadOnlyList<TypeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TypeDto>>> Apartments(CancellationToken ct)
        => Ok(await refs.GetApartmentTypesAsync(ct));

    /// <summary>Типы комнат внутри помещения.</summary>
    [HttpGet("rooms")]
    [ProducesResponseType(typeof(IReadOnlyList<TypeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TypeDto>>> Rooms(CancellationToken ct)
        => Ok(await refs.GetRoomTypesAsync(ct));
}
