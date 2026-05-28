using Fias.Service.Updater.Options;
using Fias.Service.Updater.Services.Archives;
using Fias.Service.Updater.Services.Downloading;
using Fias.Service.Updater.Services.Importing;
using Fias.Service.Updater.Services.Schema;
using Fias.Service.Updater.Services.State;
using Microsoft.Extensions.Options;

namespace Fias.Service.Updater.Services;

public interface IFiasImportOrchestrator
{
    Task RunFullAsync(string? localZipPath, CancellationToken ct);
    Task RunDeltaAsync(CancellationToken ct);
}

public class FiasImportOrchestrator(
    IMigrator migrator,
    IFiasFnsClient fnsClient,
    IFiasDownloader downloader,
    IFiasArchiveReader archiveReader,
    IFiasEntityImporterRegistry importers,
    IFiasVersionStore versionStore,
    IOptions<FiasOptions> options,
    ILogger<FiasImportOrchestrator> logger) : IFiasImportOrchestrator
{
    private readonly FiasOptions _options = options.Value;

    public async Task RunFullAsync(string? localZipPath, CancellationToken ct)
    {
        await migrator.EnsureSchemaAsync(ct);

        var zipPath = await ResolveFullArchivePathAsync(localZipPath, ct);
        logger.LogInformation("Полный импорт ФИАС из {Path}", zipPath);

        // Полная перезаливка — чистим все таблицы базового набора, чтобы COPY не упирался в PK.
        await migrator.TruncateAllAsync(ct);

        await ProcessArchiveAsync(zipPath, ImportMode.Full, ct);

        var info = await fnsClient.GetLastAsync(ct);
        await versionStore.SetVersionAsync(info, ct);
        logger.LogInformation("Полный импорт завершён, версия {VersionId}", info.VersionId);
    }

    public async Task RunDeltaAsync(CancellationToken ct)
    {
        await migrator.EnsureSchemaAsync(ct);

        var info = await fnsClient.GetLastAsync(ct);
        var current = await versionStore.GetCurrentVersionAsync(ct);

        if (current is null)
        {
            logger.LogWarning("Версия ещё не зафиксирована — дельту накатывать не на что. Запустите full-data-import");
            return;
        }
        if (current >= info.VersionId)
        {
            logger.LogInformation("Уже на последней версии {VersionId}, дельта не требуется", current);
            return;
        }

        var zipPath = await downloader.EnsureDeltaAsync(info, ct);
        logger.LogInformation("Применение дельты {From} → {To} из {Path}", current, info.VersionId, zipPath);

        await ProcessArchiveAsync(zipPath, ImportMode.Delta, ct);
        await versionStore.SetVersionAsync(info, ct);

        logger.LogInformation("Дельта применена, версия {VersionId}", info.VersionId);
    }

    private async Task<string> ResolveFullArchivePathAsync(string? localZipPath, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(localZipPath) && File.Exists(localZipPath))
            return localZipPath;

        // Если параметр не указан — пробуем стандартный путь в ImportDirectory ("файл из под ног").
        var defaultPath = Path.Combine(_options.ImportDirectory, _options.FullArchiveFileName);
        if (File.Exists(defaultPath))
        {
            logger.LogInformation("Используем локальный файл {Path}", defaultPath);
            return defaultPath;
        }

        logger.LogInformation("Локальный файл не найден, скачиваем полную выгрузку с ФНС");
        var info = await fnsClient.GetLastAsync(ct);
        return await downloader.EnsureFullAsync(info, ct);
    }

    private async Task ProcessArchiveAsync(string zipPath, ImportMode mode, CancellationToken ct)
    {
        // Перед полной заливкой очищаем целевые таблицы один раз — все импортёры внутри пишут только COPY.
        // (В реальном сценарии стоит делать всё в одной транзакции на сущность, но это потребует
        //  переработки контракта импортёра; в первой версии — простое решение.)
        var truncatedTables = new HashSet<string>();

        foreach (var entry in archiveReader.Enumerate(zipPath))
        {
            ct.ThrowIfCancellationRequested();

            var importer = importers.Resolve(entry.Kind);
            if (importer is null)
            {
                logger.LogDebug("Пропускаем {File} — нет импортёра для {Kind}", entry.FullName, entry.Kind);
                continue;
            }

            logger.LogInformation("→ {File} [{Kind}, region={Region}]", entry.FullName, entry.Kind, entry.RegionCode ?? "-");

            await using var stream = entry.OpenStream();
            await importer.ImportAsync(stream, mode, ct);
        }
    }
}
