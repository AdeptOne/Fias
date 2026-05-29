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
    /// <summary>Автокомплит адреса. Фильтры уровней (from_bound/to_bound) и области (region_code/parent_id).</summary>
    /// <param name="query">Строка ввода (минимум 2 символа).</param>
    /// <param name="count">Максимум подсказок, 1..20.</param>
    /// <param name="fromBound">Нижняя граница уровня: region|area|city|settlement|street|house|flat.</param>
    /// <param name="toBound">Верхняя граница уровня (тех же значений).</param>
    /// <param name="regionCode">Ограничение кодом субъекта РФ.</param>
    /// <param name="parentId">Ограничение поддеревом OBJECTID.</param>
    [HttpGet("address")]
    [ProducesResponseType(typeof(SuggestionsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<SuggestionsResponse>> Address(
        [FromQuery] string query,
        [FromQuery] int count = 10,
        [FromQuery(Name = "from_bound")] string? fromBound = null,
        [FromQuery(Name = "to_bound")] string? toBound = null,
        [FromQuery(Name = "region_code")] int? regionCode = null,
        [FromQuery(Name = "parent_id")] long? parentId = null,
        CancellationToken ct = default)
        => Ok(await search.SuggestAsync(query, count, fromBound, toBound, regionCode, parentId, ct));

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
