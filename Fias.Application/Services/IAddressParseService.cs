using Fias.Application.Models;

namespace Fias.Application.Services;

public interface IAddressParseService
{
    Task<ParseResult> ParseAsync(ParseRequest request, CancellationToken ct);
    Task<ParseBatchResult> ParseBatchAsync(ParseBatchRequest request, CancellationToken ct);
}
