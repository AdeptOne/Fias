using Fias.Application.Models;
using Fias.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fias.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/addresses")]
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
    /// <param name="query">Строка: адрес или FIAS GUID (минимум 2 символа).</param>
    /// <param name="limit">Максимум результатов.</param>
    [HttpGet("search")]
    [ProducesResponseType(typeof(ListResponse<AddressSearchResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ListResponse<AddressSearchResultDto>>> Search(
        [FromQuery] string query,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var results = await search.SearchAsync(query, limit, ct);
        return Ok(new ListResponse<AddressSearchResultDto>(results));
    }

    /// <summary>
    /// Поиск с полной структурой ГАР по каждому найденному объекту.
    /// Возвращает { "items": [...] } — для каждого совпадения полный адрес с иерархией,
    /// реквизитами (ОКАТО/ОКТМО/индекс/ИФНС/кадастр) и федеральным округом.
    /// </summary>
    /// <param name="query">Строка: адрес или FIAS GUID (минимум 2 символа).</param>
    /// <param name="limit">Максимум результатов.</param>
    [HttpGet("search/full")]
    [ProducesResponseType(typeof(ListResponse<AddressDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ListResponse<AddressDto>>> SearchFull(
        [FromQuery] string query,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        var results = await search.SearchAddressesAsync(query, limit, ct);
        return Ok(new ListResponse<AddressDto>(results));
    }

    /// <summary>Дочерние элементы адм. деления, с фильтрами по уровню и наименованию + пагинацией.</summary>
    [HttpGet("{objectId:long}/children")]
    [ProducesResponseType(typeof(ListResponse<AddressChildDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ListResponse<AddressChildDto>>> GetChildren(
        long objectId,
        [FromQuery] int? level = null,
        [FromQuery] string? name = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var children = await search.GetChildrenAsync(objectId, level, name, page, pageSize, ct);
        return Ok(new ListResponse<AddressChildDto>(children));
    }

    /// <summary>Путь к корню (хлебные крошки) от выбранного объекта.</summary>
    [HttpGet("{objectId:long}/parents")]
    [ProducesResponseType(typeof(ListResponse<AddressHierarchyItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ListResponse<AddressHierarchyItemDto>>> GetParents(long objectId, CancellationToken ct)
    {
        var parents = await search.GetParentsAsync(objectId, ct);
        return parents.Count == 0 ? NotFound() : Ok(new ListResponse<AddressHierarchyItemDto>(parents));
    }
}
