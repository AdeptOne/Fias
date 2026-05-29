using Fias.Service.Updater.Options;
using Fias.Service.Updater.Services.Progress;
using Microsoft.Extensions.Options;

namespace Fias.Service.Updater.Services.Downloading;

public interface IFiasDownloader
{
    /// <summary>Возвращает путь к локальному ZIP с дельтой (скачивает если нужно).</summary>
    Task<string> EnsureDeltaAsync(DownloadFileInfo info, IProgressSink progress, CancellationToken ct);
}

public class FiasDownloader(
    HttpClient http,
    IOptions<FiasOptions> options,
    ILogger<FiasDownloader> logger) : IFiasDownloader
{
    private readonly FiasOptions _options = options.Value;

    public Task<string> EnsureDeltaAsync(DownloadFileInfo info, IProgressSink progress, CancellationToken ct)
    {
        var url = info.GarXmlDeltaUrl
                  ?? $"{_options.ActualDownloadsBaseUrl.TrimEnd('/')}/{_options.DeltaArchiveFileName}";
        var target = Path.Combine(_options.ImportDirectory, $"gar_delta_xml_{info.VersionId}.zip");
        return DownloadIfMissingAsync(url, target, progress, ct);
    }

    private async Task<string> DownloadIfMissingAsync(string url, string targetPath, IProgressSink progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        if (File.Exists(targetPath))
        {
            var msg = $"Файл уже скачан: {targetPath}";
            logger.LogInformation(msg);
            progress.WriteLine(msg);
            return targetPath;
        }

        var tempPath = targetPath + ".part";
        var startMsg = $"Скачивание {url} → {targetPath}";
        logger.LogInformation(startMsg);
        progress.WriteLine(startMsg);

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using (var src = await response.Content.ReadAsStreamAsync(ct))
        await using (var dst = new FileStream(
                         tempPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 1 << 20,            // 1 МБ disk-буфер
                         useAsync: true))
        {
            await CopyWithProgressAsync(src, dst, total, targetPath, progress, ct);
        }

        File.Move(tempPath, targetPath, overwrite: true);
        var doneMsg = $"Скачано: {targetPath} ({new FileInfo(targetPath).Length:N0} байт)";
        logger.LogInformation(doneMsg);
        progress.WriteLine(doneMsg);
        return targetPath;
    }

    /// <summary>
    /// Копирует поток буферами по 1 МБ. Прогресс-бар и WriteLine обновляются ВМЕСТЕ
    /// и не чаще, чем раз в 3 секунды или каждые 50 МБ — каждое SetValue Hangfire.Console
    /// делает INSERT в Postgres, и слишком частые вызовы (на каждом мелком батче)
    /// душат пропускную способность скачивания на многогигабайтных файлах.
    /// </summary>
    private static async Task CopyWithProgressAsync(
        Stream src, Stream dst, long? total, string targetPath, IProgressSink progress, CancellationToken ct)
    {
        const int bufferSize = 1 << 20;                  // 1 МБ — крупнее системные вызовы
        const long reportEveryBytes = 50L * 1024 * 1024;
        var reportEveryInterval = TimeSpan.FromSeconds(3);

        var buffer = new byte[bufferSize];
        long copied = 0;
        long lastReportedBytes = 0;
        var started = DateTime.UtcNow;
        var lastReportedAt = started;
        var fileName = Path.GetFileName(targetPath);

        var bar = total is { } sizeForBar && sizeForBar > 0
            ? progress.StartProgressBar($"Скачивание {fileName}")
            : null;

        int read;
        while ((read = await src.ReadAsync(buffer.AsMemory(0, bufferSize), ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            copied += read;

            var now = DateTime.UtcNow;
            if (copied - lastReportedBytes < reportEveryBytes && now - lastReportedAt < reportEveryInterval)
                continue;

            var elapsed = now - started;
            var speedMBs = elapsed.TotalSeconds > 0
                ? copied / (1024d * 1024d) / elapsed.TotalSeconds
                : 0;

            if (total is { } known && known > 0)
            {
                var percent = copied * 100d / known;
                var eta = speedMBs > 0 ? (known - copied) / (speedMBs * 1024 * 1024) : 0;
                progress.WriteLine(
                    $"{fileName}: {copied:N0} / {known:N0} байт ({percent:F1}%), {speedMBs:F1} MB/s, ETA {eta:F0}s");
                bar?.SetValue(percent);
            }
            else
            {
                progress.WriteLine($"{fileName}: {copied:N0} байт, {speedMBs:F1} MB/s");
            }

            lastReportedBytes = copied;
            lastReportedAt = now;
        }

        bar?.SetValue(100);
    }
}
