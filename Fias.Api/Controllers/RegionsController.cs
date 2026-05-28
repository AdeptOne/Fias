using Fias.Application.Models;
using Fias.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fias.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/regions")]
[Produces("application/json")]
public class RegionsController(IHierarchyService hierarchy) : ControllerBase
{
    /// <summary>Все субъекты РФ (level=1), активные.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<RegionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RegionDto>>> GetAll(CancellationToken ct)
        => Ok(await hierarchy.GetRegionsAsync(ct));
}
