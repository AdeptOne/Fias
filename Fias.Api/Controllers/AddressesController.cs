using Fias.Application.Models;
using Fias.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fias.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AddressesController(
    IAddressBuilderService builder,
    IAddressSearchService search) : ControllerBase
{
    /// <summary>Полная адресная строка и иерархия по OBJECTID ФИАС.</summary>
    [HttpGet("{objectId:long}")]
    [ProducesResponseType(typeof(AddressDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AddressDto>> GetByObjectId(long objectId, CancellationToken ct)
    {
        var address = await builder.BuildByObjectIdAsync(objectId, ct);
        return address is null ? NotFound() : Ok(address);
    }

    /// <summary>Полная адресная строка по OBJECTGUID активного адресного объекта.</summary>
    [HttpGet("by-guid/{objectGuid:guid}")]
    [ProducesResponseType(typeof(AddressDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AddressDto>> GetByObjectGuid(Guid objectGuid, CancellationToken ct)
    {
        var address = await builder.BuildByObjectGuidAsync(objectGuid, ct);
        return address is null ? NotFound() : Ok(address);
    }

    /// <summary>
    /// Нечёткий поиск по наименованию (pg_trgm). Допускает опечатки.
    /// </summary>
    /// <param name="q">Строка поиска (минимум 2 символа).</param>
    /// <param name="limit">Максимум результатов, 1..100.</param>
    /// <param name="threshold">Порог similarity, 0.1..1.0. Чем выше, тем строже.</param>
    [HttpGet("search")]
    [ProducesResponseType(typeof(IReadOnlyList<AddressSearchResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AddressSearchResultDto>>> Search(
        [FromQuery] string q,
        [FromQuery] int limit = 20,
        [FromQuery] double threshold = 0.3,
        CancellationToken ct = default)
    {
        var results = await search.SearchAsync(q, limit, threshold, ct);
        return Ok(results);
    }

    /// <summary>Дочерние элементы по адресной иерархии (адм. деление).</summary>
    [HttpGet("{objectId:long}/children")]
    [ProducesResponseType(typeof(IReadOnlyList<AddressChildDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AddressChildDto>>> GetChildren(long objectId, CancellationToken ct)
    {
        var children = await search.GetChildrenAsync(objectId, ct);
        return Ok(children);
    }
}
