using Fias.Application.Models;
using Fias.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fias.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/streets")]
[Produces("application/json")]
public class StreetsController(IHierarchyService hierarchy) : ControllerBase
{
    /// <summary>Дома (level=10) на улице (level=8).</summary>
    /// <param name="objectId">OBJECTID улицы.</param>
    /// <param name="num">Опциональный фильтр по номеру дома (substring).</param>
    /// <param name="page">Номер страницы, начиная с 1.</param>
    /// <param name="pageSize">Размер страницы, 1..500.</param>
    [HttpGet("{objectId:long}/houses")]
    [ProducesResponseType(typeof(PagedResult<HouseSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<HouseSummaryDto>>> GetHouses(
        long objectId,
        [FromQuery] string? num = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
        => Ok(await hierarchy.GetHousesByStreetAsync(objectId, num, page, pageSize, ct));
}
