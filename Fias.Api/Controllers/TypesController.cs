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
    [ProducesResponseType(typeof(ListResponse<TypeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ListResponse<TypeDto>>> AddressObjects(
        [FromQuery] int? level = null, CancellationToken ct = default)
        => Ok(new ListResponse<TypeDto>(await refs.GetAddressObjectTypesAsync(level, ct)));

    /// <summary>Типы зданий (д., стр., корп.).</summary>
    [HttpGet("houses")]
    [ProducesResponseType(typeof(ListResponse<TypeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ListResponse<TypeDto>>> Houses(CancellationToken ct)
        => Ok(new ListResponse<TypeDto>(await refs.GetHouseTypesAsync(ct)));

    /// <summary>Типы помещений (кв., оф., пом.).</summary>
    [HttpGet("apartments")]
    [ProducesResponseType(typeof(ListResponse<TypeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ListResponse<TypeDto>>> Apartments(CancellationToken ct)
        => Ok(new ListResponse<TypeDto>(await refs.GetApartmentTypesAsync(ct)));

    /// <summary>Типы комнат внутри помещения.</summary>
    [HttpGet("rooms")]
    [ProducesResponseType(typeof(ListResponse<TypeDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ListResponse<TypeDto>>> Rooms(CancellationToken ct)
        => Ok(new ListResponse<TypeDto>(await refs.GetRoomTypesAsync(ct)));
}
