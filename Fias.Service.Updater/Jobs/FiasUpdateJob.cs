using Hangfire;

namespace Fias.Service.Updater.Jobs;

public enum UpdateType { Full, Delta }

public class FiasUpdateJob(ILogger<FiasUpdateJob> logger)
{
    [Queue("fias")]
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [300, 600, 1800])]
    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    public Task RunAsync(UpdateType type, CancellationToken ct)
    {
        logger.LogInformation("Обновление БД ФИАС [{Type}] - старт", type);
        
        return Task.CompletedTask;
    }
}