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
    /// <param name="q">Строка поиска (минимум 2 символа).</param>
    /// <param name="limit">Максимум результатов, 1..100.</param>
    /// <param name="threshold">Порог similarity, 0.1..1.0. Чем выше, тем строже.</param>
    /// <param name="level">Опциональный фильтр по уровню (1=регион, 8=улица, ...).</param>
    /// <param name="parentId">Опциональный OBJECTID родителя — ограничивает поиск его поддеревом.</param>
    [HttpGet("search")]
    [ProducesResponseType(typeof(IReadOnlyList<AddressSearchResultDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AddressSearchResultDto>>> Search(
        [FromQuery] string q,
        [FromQuery] int limit = 20,
        [FromQuery] double threshold = 0.3,
        [FromQuery] int? level = null,
        [FromQuery] long? parentId = null,
        CancellationToken ct = default)
    {
        var results = await search.SearchAsync(q, limit, threshold, level, parentId, ct);
        return Ok(results);
    }

    /// <summary>
    /// Нечёткий поиск с полной структурой ГАР по каждому найденному объекту.
    /// Возвращает { "addresses": [...] } — для каждого совпадения полный адрес с иерархией,
    /// реквизитами (ОКАТО/ОКТМО/индекс/ИФНС/кадастр) и федеральным округом.
    /// </summary>
    /// <param name="q">Строка поиска (минимум 2 символа).</param>
    /// <param name="limit">Максимум результатов, 1..100.</param>
    /// <param name="threshold">Порог similarity, 0.1..1.0. Чем выше, тем строже.</param>
    /// <param name="level">Опциональный фильтр по уровню (1=регион, 8=улица, ...).</param>
    /// <param name="parentId">Опциональный OBJECTID родителя — ограничивает поиск его поддеревом.</param>
    [HttpGet("search/full")]
    [ProducesResponseType(typeof(AddressListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AddressListResponse>> SearchFull(
        [FromQuery] string q,
        [FromQuery] int limit = 20,
        [FromQuery] double threshold = 0.3,
        [FromQuery] int? level = null,
        [FromQuery] long? parentId = null,
        CancellationToken ct = default)
    {
        var results = await search.SearchAddressesAsync(q, limit, threshold, level, parentId, ct);
        return Ok(results);
    }

    /// <summary>Дочерние элементы адм. деления, с фильтрами по уровню и наименованию + пагинацией.</summary>
    [HttpGet("{objectId:long}/children")]
    [ProducesResponseType(typeof(IReadOnlyList<AddressChildDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<AddressChildDto>>> GetChildren(
        long objectId,
        [FromQuery] int? level = null,
        [FromQuery] string? name = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var children = await search.GetChildrenAsync(objectId, level, name, page, pageSize, ct);
        return Ok(children);
    }

    /// <summary>Путь к корню (хлебные крошки) от выбранного объекта.</summary>
    [HttpGet("{objectId:long}/parents")]
    [ProducesResponseType(typeof(IReadOnlyList<AddressHierarchyItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<AddressHierarchyItemDto>>> GetParents(long objectId, CancellationToken ct)
    {
        var parents = await search.GetParentsAsync(objectId, ct);
        return parents.Count == 0 ? NotFound() : Ok(parents);
    }
}
