using Fias.Service.Updater.Jobs;
using Hangfire;

namespace Fias.Service.Updater;

public class Worker(IRecurringJobManager jobs) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        jobs.AddOrUpdate<FiasUpdateJob>(
            "full-data-import", "fias", 
            j => j.RunAsync(UpdateType.Full, stoppingToken),
            Cron.Never());
        
        jobs.AddOrUpdate<FiasUpdateJob>(
            "delta-data-import", "fias", 
            j => j.RunAsync(UpdateType.Delta, stoppingToken), 
            Cron.Daily(3));
        
        return Task.CompletedTask;
    }
}