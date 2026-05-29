using System.Collections.Concurrent;
using Fias.Service.Updater.Options;
using Fias.Service.Updater.Services.Archives;
using Fias.Service.Updater.Services.Downloading;
using Fias.Service.Updater.Services.Importing;
using Fias.Service.Updater.Services.Progress;
using Fias.Service.Updater.Services.Schema;
using Fias.Service.Updater.Services.Search;
using Fias.Service.Updater.Services.State;
using Microsoft.Extensions.Options;

namespace Fias.Service.Updater.Services;

public interface IFiasImportOrchestrator
{
    Task RunFullAsync(string? localZipPath, IProgressSink progress, CancellationToken ct);
    Task RunDeltaAsync(IProgressSink progress, CancellationToken ct);
}

public class FiasImportOrchestrator(
    IMigrator migrator,
    IFiasFnsClient fnsClient,
    IFiasDownloader downloader,
    IFiasArchiveReader archiveReader,
    IFiasEntityImporterRegistry importers,
    ISearchProjectionBuilder searchProjection,
    IFiasVersionStore versionStore,
    IOptions<FiasOptions> options,
    ILogger<FiasImportOrchestrator> logger) : IFiasImportOrchestrator
{
    private readonly FiasOptions _options = options.Value;

    public async Task RunFullAsync(string? localZipPath, IProgressSink progress, CancellationToken ct)
    {
        await migrator.EnsureSchemaAsync(ct);

        var zipPath = await ResolveFullArchivePathAsync(localZipPath, progress, ct);
        progress.WriteLine($"Полный импорт ФИАС из {zipPath}");
        logger.LogInformation("Полный импорт ФИАС из {Path}", zipPath);

        // Полная перезаливка — чистим все таблицы базового набора, чтобы COPY не упирался в PK.
        await migrator.TruncateAllAsync(ct);

        // Снимаем вторичные индексы fias.* на время COPY: их поддержка на каждой вставке (особенно
        // GIN name_trgm) — главный тормоз при параллельной заливке. Построим разом после.
        progress.WriteLine("Снятие вторичных индексов fias.* перед заливкой");
        await migrator.DropSecondaryIndexesAsync(ct);

        await ProcessArchiveAsync(zipPath, ImportMode.Full, progress, ct);

        progress.WriteLine("Построение вторичных индексов fias.* (после заливки)");
        await migrator.RebuildSecondaryIndexesAsync(Math.Clamp(_options.ImportParallelism, 1, 16), ct);

        // Денормализованную проекцию для поиска собираем после заливки сырых данных.
        await searchProjection.RebuildAsync(progress, ct);

        var info = await fnsClient.GetLastAsync(ct);
        await versionStore.SetVersionAsync(info, ct);
        progress.WriteLine($"Полный импорт завершён, версия {info.VersionId}");
        logger.LogInformation("Полный импорт завершён, версия {VersionId}", info.VersionId);
    }

    public async Task RunDeltaAsync(IProgressSink progress, CancellationToken ct)
    {
        await migrator.EnsureSchemaAsync(ct);

        var info = await fnsClient.GetLastAsync(ct);
        var current = await versionStore.GetCurrentVersionAsync(ct);

        if (current is null)
        {
            progress.WriteLine("Версия ещё не зафиксирована — дельту накатывать не на что");
            logger.LogWarning("Версия ещё не зафиксирована — дельту накатывать не на что. Запустите full-data-import");
            return;
        }
        if (current >= info.VersionId)
        {
            progress.WriteLine($"Уже на последней версии {current}, дельта не требуется");
            logger.LogInformation("Уже на последней версии {VersionId}, дельта не требуется", current);
            return;
        }

        var zipPath = await downloader.EnsureDeltaAsync(info, progress, ct);
        progress.WriteLine($"Применение дельты {current} → {info.VersionId} из {zipPath}");
        logger.LogInformation("Применение дельты {From} → {To} из {Path}", current, info.VersionId, zipPath);

        await ProcessArchiveAsync(zipPath, ImportMode.Delta, progress, ct);

        // После дельты пересобираем проекцию целиком (v1) — гарантированно консистентно.
        await searchProjection.RebuildAsync(progress, ct);

        await versionStore.SetVersionAsync(info, ct);

        progress.WriteLine($"Дельта применена, версия {info.VersionId}");
        logger.LogInformation("Дельта применена, версия {VersionId}", info.VersionId);
    }

    private Task<string> ResolveFullArchivePathAsync(string? localZipPath, IProgressSink progress, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(localZipPath) && File.Exists(localZipPath))
            return Task.FromResult(localZipPath);

        // Если параметр не указан — пробуем стандартный путь в ImportDirectory ("файл из под ног").
        var defaultPath = Path.Combine(_options.ImportDirectory, _options.FullArchiveFileName);
        if (File.Exists(defaultPath))
        {
            progress.WriteLine($"Используем локальный файл {defaultPath}");
            logger.LogInformation("Используем локальный файл {Path}", defaultPath);
            return Task.FromResult(defaultPath);
        }

        // Полная выгрузка только из локального архива — автоскачивание с ФНС отключено.
        var message =
            $"Локальный архив полной выгрузки не найден. Укажите путь явно или положите файл " +
            $"'{_options.FullArchiveFileName}' в каталог '{_options.ImportDirectory}'.";
        progress.WriteLine(message);
        logger.LogError(message);
        throw new FileNotFoundException(message, defaultPath);
    }

    private async Task ProcessArchiveAsync(string zipPath, ImportMode mode, IProgressSink progress, CancellationToken ct)
    {
        // Таблицы уже очищены один раз в RunFullAsync (TruncateAllAsync). Импортёры — stateless
        // синглтоны, каждый ImportAsync открывает своё соединение и пишет COPY/UPSERT независимо,
        // поэтому файлы можно лить параллельно.
        var entries = archiveReader.Enumerate(zipPath)
            .Where(e => importers.Resolve(e.Kind) is not null)
            .ToList();

        if (entries.Count == 0)
        {
            progress.WriteLine("В архиве нет файлов с известными импортёрами");
            return;
        }

        var parallelism = Math.Clamp(_options.ImportParallelism, 1, 16);
        parallelism = Math.Min(parallelism, entries.Count);
        progress.WriteLine($"Импорт {entries.Count} файлов, параллелизм {parallelism}");

        // Work-stealing: общая очередь файлов, у каждого воркера — СВОЙ ZipArchive (он не
        // потокобезопасен) и собственное соединение через ImportAsync.
        var queue = new ConcurrentQueue<FiasArchiveEntry>(entries);
        var processed = 0;

        var workers = Enumerable.Range(0, parallelism).Select(_ => Task.Run(async () =>
        {
            using var accessor = archiveReader.OpenAccessor(zipPath);
            while (queue.TryDequeue(out var entry))
            {
                ct.ThrowIfCancellationRequested();
                var importer = importers.Resolve(entry.Kind)!;

                await using (var stream = accessor.Open(entry.FullName))
                    await importer.ImportAsync(stream, mode, ct);

                var n = Interlocked.Increment(ref processed);
                progress.WriteLine($"[{n}/{entries.Count}] {entry.FullName} [{entry.Kind}, region={entry.RegionCode ?? "-"}]");
            }
        }, ct)).ToArray();

        await Task.WhenAll(workers);
    }
}
