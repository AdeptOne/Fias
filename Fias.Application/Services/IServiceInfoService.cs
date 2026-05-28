using Fias.Application.Models;

namespace Fias.Application.Services;

public interface IServiceInfoService
{
    Task<VersionDto> GetVersionAsync(CancellationToken ct);
    Task<StatsDto> GetStatsAsync(CancellationToken ct);
}
