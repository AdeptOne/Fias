using Fias.Application.Services;
using Fias.Service.Updater.Services;
using Fias.Service.Updater.Services.Progress;
using Hangfire;

namespace Fias.Service.Updater.Jobs;

public class FiasUpdateJob(IFiasImportOrchestrator orchestrator, ILogger<FiasUpdateJob> logger) : IFiasUpdateJob
{
    /// <summary>
    /// Полная загрузка ФИАС. Если <paramref name="localZipPath"/> указан и существует —
    /// читается локальный архив (volume); иначе ищется gar_xml.zip в FiasOptions.ImportDirectory;
    /// если и его нет — скачивается с сайта ФНС. Очередь fias — из атрибута на интерфейсе.
    /// </summary>
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = [600, 1800])]
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60 * 6)]
    public Task RunFullAsync(string? localZipPath, CancellationToken ct)
    {
        logger.LogInformation("Полный импорт ФИАС — старт (path={Path})", localZipPath ?? "<auto>");
        return orchestrator.RunFullAsync(localZipPath, new LoggerProgressSink(logger), ct);
    }

    /// <summary>
    /// Применение последней дельты от ФНС. Идемпотентно: если уже на последней версии — выходит без действий.
    /// </summary>
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [300, 600, 1800])]
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    public Task RunDeltaAsync(CancellationToken ct)
    {
        logger.LogInformation("Дельта ФИАС — старт");
        return orchestrator.RunDeltaAsync(new LoggerProgressSink(logger), ct);
    }
}
