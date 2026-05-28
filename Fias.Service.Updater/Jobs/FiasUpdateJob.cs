using Fias.Application.Services;
using Fias.Service.Updater.Services;
using Fias.Service.Updater.Services.Progress;
using Hangfire;
using Hangfire.Server;

namespace Fias.Service.Updater.Jobs;

public class FiasUpdateJob(IFiasImportOrchestrator orchestrator, ILogger<FiasUpdateJob> logger) : IFiasUpdateJob
{
    /// <summary>
    /// Полная загрузка ФИАС. Если <paramref name="localZipPath"/> указан и существует —
    /// читается локальный архив (volume); иначе ищется gar_xml.zip в FiasOptions.ImportDirectory;
    /// если и его нет — скачивается с сайта ФНС. PerformContext инжектится Hangfire'ом —
    /// если он не null, прогресс публикуется в Console-вкладку дашборда.
    /// </summary>
    [Queue("fias")]
    [AutomaticRetry(Attempts = 2, DelaysInSeconds = [600, 1800])]
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60 * 6)]
    public Task RunFullAsync(string? localZipPath, PerformContext? context, CancellationToken ct)
    {
        logger.LogInformation("Полный импорт ФИАС — старт (path={Path})", localZipPath ?? "<auto>");
        var sink = BuildSink(context);
        return orchestrator.RunFullAsync(localZipPath, sink, ct);
    }

    /// <summary>Перегрузка для вызовов через интерфейс <see cref="IFiasUpdateJob"/> (без PerformContext).</summary>
    public Task RunFullAsync(string? localZipPath, CancellationToken ct)
        => RunFullAsync(localZipPath, context: null, ct);

    /// <summary>
    /// Применение последней дельты от ФНС. Идемпотентно: если уже на последней версии — выходит без действий.
    /// </summary>
    [Queue("fias")]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [300, 600, 1800])]
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    public Task RunDeltaAsync(PerformContext? context, CancellationToken ct)
    {
        logger.LogInformation("Дельта ФИАС — старт");
        var sink = BuildSink(context);
        return orchestrator.RunDeltaAsync(sink, ct);
    }

    /// <summary>Перегрузка для вызовов через интерфейс (без PerformContext).</summary>
    public Task RunDeltaAsync(CancellationToken ct)
        => RunDeltaAsync(context: null, ct);

    private IProgressSink BuildSink(PerformContext? context)
        => context is null ? new LoggerProgressSink(logger) : new HangfireConsoleProgressSink(context);
}
