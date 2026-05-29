using Fias.Application.Models;
using Fias.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fias.Api.Controllers;

/// <summary>Стандартизация адресной строки: сырой ввод → один лучший разобранный адрес + qc/confidence.</summary>
[ApiController]
[Authorize]
[Route("api/v1/clean")]
[Produces("application/json")]
public class CleanController(IAddressSearchService search) : ControllerBase
{
    /// <summary>Тело запроса стандартизации.</summary>
    public record CleanAddressRequest(string Query);

    [HttpPost("address")]
    [ProducesResponseType(typeof(CleanResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<CleanResultDto>> Address([FromBody] CleanAddressRequest request, CancellationToken ct)
        => Ok(await search.CleanAsync(request.Query, ct));
}
