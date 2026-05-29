using Fias.Application.Services;
using Fias.Service.Updater.Services;
using Fias.Service.Updater.Services.Progress;
using Hangfire;

namespace Fias.Service.Updater.Jobs;

public class FiasUpdateJob(IFiasImportOrchestrator orchestrator, ILogger<FiasUpdateJob> logger) : IFiasUpdateJob
{
    /// <summary>
    /// Полная загрузка ФИАС. Если <paramref name="localZipPath"/> указан и существует —
    /// читается локальный архив (volume); иначе ищется gar_xml.zip в FiasOptions.ImportDirectory.
    /// Автоскачивание полной выгрузки с ФНС отключено — нужен локальный файл. Очередь fias —
    /// из атрибута на интерфейсе.
    /// </summary>
    /// <remarks>
    /// Авто-ретраи отключены (Attempts = 0): импорт долгий и идемпотентный, любая ошибка иначе
    /// выбрасывала бы многочасовую работу и запускала всё заново. DisableConcurrentExecution
    /// защищает от двух одновременных полных импортов; таймаут ожидания лока мал, чтобы случайный
    /// дубль падал сразу, а не висел до освобождения и не перезапускал импорт. Время жизни самой
    /// джобы этим атрибутом НЕ ограничивается — за это отвечает UseSlidingInvisibilityTimeout
    /// в конфигурации Hangfire.
    /// </remarks>
    [AutomaticRetry(Attempts = 0)]
    [DisableConcurrentExecution(timeoutInSeconds: 10)]
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
