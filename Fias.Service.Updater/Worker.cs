using Fias.Service.Updater.Jobs;
using Hangfire;

namespace Fias.Service.Updater;

public class Worker(IRecurringJobManager jobs) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Полная загрузка запускается вручную из Hangfire Dashboard (Trigger now).
        // При пустом localZipPath оркестратор сам решит: локальный файл из ImportDirectory или скачать с ФНС.
        jobs.AddOrUpdate<FiasUpdateJob>(
            "full-data-import",
            "fias",
            j => j.RunFullAsync(null, CancellationToken.None),
            Cron.Never());

        jobs.AddOrUpdate<FiasUpdateJob>(
            "delta-data-import",
            "fias",
            j => j.RunDeltaAsync(CancellationToken.None),
            Cron.Daily(3));

        return Task.CompletedTask;
    }
}
