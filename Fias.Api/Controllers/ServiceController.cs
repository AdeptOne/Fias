using Fias.Application.Models;
using Fias.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fias.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public class ServiceController(IServiceInfoService info) : ControllerBase
{
    /// <summary>Текущая версия выгрузки ФИАС в БД (VersionId, дата экспорта, дата применения).</summary>
    [HttpGet("version")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(VersionDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<VersionDto>> Version(CancellationToken ct)
        => Ok(await info.GetVersionAsync(ct));

    /// <summary>Liveness/readiness — простая проверка доступности БД.</summary>
    [HttpGet("health")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public IActionResult Health() => Ok(new { status = "ok" });

    /// <summary>Подсчёт активных записей по основным таблицам.</summary>
    [HttpGet("stats")]
    [Authorize]
    [ProducesResponseType(typeof(StatsDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<StatsDto>> Stats(CancellationToken ct)
        => Ok(await info.GetStatsAsync(ct));
}
