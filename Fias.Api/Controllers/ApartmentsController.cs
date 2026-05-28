using Fias.Application.Models;
using Fias.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fias.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/apartments")]
[Produces("application/json")]
public class ApartmentsController(IHierarchyService hierarchy) : ControllerBase
{
    /// <summary>Комнаты (level=12) в помещении (level=11).</summary>
    [HttpGet("{objectId:long}/rooms")]
    [ProducesResponseType(typeof(PagedResult<RoomSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<RoomSummaryDto>>> GetRooms(
        long objectId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
        => Ok(await hierarchy.GetRoomsByApartmentAsync(objectId, page, pageSize, ct));
}
