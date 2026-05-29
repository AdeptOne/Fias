using Fias.Application.Services;
using Fias.Service.Updater.Services.Schema;
using Hangfire;

namespace Fias.Service.Updater;

public class Worker(
    IServiceScopeFactory scopeFactory,
    IRecurringJobManager jobs,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Создаём схему fias.* при старте, чтобы Api сразу мог читать import_state и т.п.
        // (Раньше миграция запускалась только из job'а — пустая БД ломала /version в Api.)
        try
        {
            using var scope = scopeFactory.CreateScope();
            var migrator = scope.ServiceProvider.GetRequiredService<IMigrator>();
            await migrator.EnsureSchemaAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось применить схему при старте");
            throw;
        }

        // Полная загрузка запускается вручную из Hangfire Dashboard (Trigger now).
        // При пустом localZipPath оркестратор сам решит: локальный файл из ImportDirectory или скачать с ФНС.
        // Регистрируем recurring job'ы через интерфейс — очередь fias подхватывается из
        // [Queue] атрибута на IFiasUpdateJob, поэтому повторы/расписания не сваливаются
        // в default.
        jobs.AddOrUpdate<IFiasUpdateJob>(
            "full-data-import",
            j => j.RunFullAsync(null, CancellationToken.None),
            Cron.Never());

        jobs.AddOrUpdate<IFiasUpdateJob>(
            "delta-data-import",
            j => j.RunDeltaAsync(CancellationToken.None),
            Cron.Daily(3));
    }
}
