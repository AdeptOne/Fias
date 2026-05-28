using Fias.Application.Services;
using Fias.Service.Updater.Services;
using Hangfire;

namespace Fias.Service.Updater.Jobs;

public class FiasUpdateJob(IFiasImportOrchestrator orchestrator, ILogger<FiasUpdateJob> logger) : IFiasUpdateJob
{
    /// <summary>
    /// Полная загрузка ФИАС. Если <paramref name="localZipPath"/> указан и существует —
    /// читается локальный архив (volume); иначе ищется gar_xml.zip в FiasOptions.ImportDirectory;
    /// если и его нет — скачивается с сайта ФНС.
    /// </summary>
    [Queue("fias")]
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = [600, 1800])]
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60 * 6)]
    public async Task RunFullAsync(string? localZipPath, CancellationToken ct)
    {
        logger.LogInformation("Полный импорт ФИАС — старт (path={Path})", localZipPath ?? "<auto>");
        await orchestrator.RunFullAsync(localZipPath, ct);
    }

    /// <summary>
    /// Применение последней дельты от ФНС. Идемпотентно: если уже на последней версии — выходит без действий.
    /// </summary>
    [Queue("fias")]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [300, 600, 1800])]
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    public async Task RunDeltaAsync(CancellationToken ct)
    {
        logger.LogInformation("Дельта ФИАС — старт");
        await orchestrator.RunDeltaAsync(ct);
    }
}
