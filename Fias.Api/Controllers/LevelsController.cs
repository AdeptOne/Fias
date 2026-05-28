using Fias.Application.Models;
using Fias.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fias.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/levels")]
[Produces("application/json")]
public class LevelsController(IReferencesService refs) : ControllerBase
{
    /// <summary>Справочник уровней объектов адресации (OBJECT_LEVELS).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<LevelDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LevelDto>>> GetAll(CancellationToken ct)
        => Ok(await refs.GetLevelsAsync(ct));
}
