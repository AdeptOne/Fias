using Fias.Application.Models;
using Fias.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fias.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/houses")]
[Produces("application/json")]
public class HousesController(IHierarchyService hierarchy) : ControllerBase
{
    /// <summary>Помещения (level=11) в доме (level=10).</summary>
    [HttpGet("{objectId:long}/apartments")]
    [ProducesResponseType(typeof(PagedResult<ApartmentSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<ApartmentSummaryDto>>> GetApartments(
        long objectId,
        [FromQuery] string? num = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
        => Ok(await hierarchy.GetApartmentsByHouseAsync(objectId, num, page, pageSize, ct));
}
