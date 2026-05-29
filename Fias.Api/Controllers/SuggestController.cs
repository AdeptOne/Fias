using Fias.Application.Models;
using Fias.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fias.Api.Controllers;

/// <summary>Саджест адреса в формате, близком к DaData (value/unrestricted_value/data).</summary>
[ApiController]
[Authorize]
[Route("api/v1/suggest")]
[Produces("application/json")]
public class SuggestController(IAddressSearchService search) : ControllerBase
{
    /// <summary>Автокомплит адреса. Принимает строку адреса ИЛИ FIAS GUID — больше ничего не нужно.</summary>
    /// <param name="query">Адрес или FIAS GUID (минимум 2 символа).</param>
    /// <param name="limit">Максимум подсказок, 1..20.</param>
    [HttpGet("address")]
    [ProducesResponseType(typeof(ListResponse<SuggestionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ListResponse<SuggestionDto>>> Address(
        [FromQuery] string query,
        [FromQuery] int limit = 10,
        CancellationToken ct = default)
        => Ok(new ListResponse<SuggestionDto>(await search.SuggestAsync(query, limit, ct)));

    /// <summary>Резолв адреса по FIAS GUID — один элемент того же формата.</summary>
    [HttpGet("address/{fiasId:guid}")]
    [ProducesResponseType(typeof(SuggestionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SuggestionDto>> AddressById(Guid fiasId, CancellationToken ct)
    {
        var suggestion = await search.SuggestByGuidAsync(fiasId, ct);
        return suggestion is null ? NotFound() : Ok(suggestion);
    }
}
