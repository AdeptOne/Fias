using Fias.Service.Updater.Options;
using Microsoft.Extensions.Options;

namespace Fias.Service.Updater.Services.Downloading;

public interface IFiasDownloader
{
    /// <summary>Возвращает путь к локальному ZIP с дельтой (скачивает если нужно).</summary>
    Task<string> EnsureDeltaAsync(DownloadFileInfo info, CancellationToken ct);

    /// <summary>Возвращает путь к локальному ZIP с полной выгрузкой (скачивает если нужно).</summary>
    Task<string> EnsureFullAsync(DownloadFileInfo info, CancellationToken ct);
}

public class FiasDownloader(
    HttpClient http,
    IOptions<FiasOptions> options,
    ILogger<FiasDownloader> logger) : IFiasDownloader
{
    private readonly FiasOptions _options = options.Value;

    public Task<string> EnsureDeltaAsync(DownloadFileInfo info, CancellationToken ct)
    {
        var url = info.GarXmlDeltaUrl
                  ?? $"{_options.ActualDownloadsBaseUrl.TrimEnd('/')}/{_options.DeltaArchiveFileName}";
        var target = Path.Combine(_options.ImportDirectory, $"gar_delta_xml_{info.VersionId}.zip");
        return DownloadIfMissingAsync(url, target, ct);
    }

    public Task<string> EnsureFullAsync(DownloadFileInfo info, CancellationToken ct)
    {
        var url = info.GarXmlFullUrl
                  ?? $"{_options.ActualDownloadsBaseUrl.TrimEnd('/')}/{_options.FullArchiveFileName}";
        var target = Path.Combine(_options.ImportDirectory, $"gar_xml_{info.VersionId}.zip");
        return DownloadIfMissingAsync(url, target, ct);
    }

    private async Task<string> DownloadIfMissingAsync(string url, string targetPath, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        if (File.Exists(targetPath))
        {
            logger.LogInformation("Файл уже скачан: {Path}", targetPath);
            return targetPath;
        }

        var tempPath = targetPath + ".part";
        logger.LogInformation("Скачивание {Url} → {Path}", url, targetPath);

        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using (var src = await response.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(tempPath))
        {
            await CopyWithProgressAsync(src, dst, total, targetPath, ct);
        }

        File.Move(tempPath, targetPath, overwrite: true);
        logger.LogInformation("Скачано: {Path} ({Length:N0} байт)", targetPath, new FileInfo(targetPath).Length);
        return targetPath;
    }

    /// <summary>
    /// Копирует поток буферами по 80 КБ, периодически логируя прогресс — раз в 3 секунды
    /// или каждые 50 МБ (что наступит раньше). Если Content-Length известен, в логе доля %.
    /// </summary>
    private async Task CopyWithProgressAsync(
        Stream src, Stream dst, long? total, string targetPath, CancellationToken ct)
    {
        const int bufferSize = 81_920;
        const long logEveryBytes = 50L * 1024 * 1024;
        var logEveryInterval = TimeSpan.FromSeconds(3);

        var buffer = new byte[bufferSize];
        long copied = 0;
        long lastLoggedBytes = 0;
        var started = DateTime.UtcNow;
        var lastLoggedAt = started;
        var fileName = Path.GetFileName(targetPath);

        int read;
        while ((read = await src.ReadAsync(buffer.AsMemory(0, bufferSize), ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            copied += read;

            var now = DateTime.UtcNow;
            var deltaBytes = copied - lastLoggedBytes;
            if (deltaBytes >= logEveryBytes || now - lastLoggedAt >= logEveryInterval)
            {
                var elapsed = now - started;
                var speedMBs = elapsed.TotalSeconds > 0
                    ? copied / (1024d * 1024d) / elapsed.TotalSeconds
                    : 0;

                if (total is { } t && t > 0)
                {
                    var percent = copied * 100d / t;
                    var etaSeconds = speedMBs > 0
                        ? (t - copied) / (speedMBs * 1024 * 1024)
                        : 0;
                    logger.LogInformation(
                        "{File}: {Copied:N0} / {Total:N0} байт ({Percent:F1}%), {Speed:F1} MB/s, ETA {Eta:F0}s",
                        fileName, copied, t, percent, speedMBs, etaSeconds);
                }
                else
                {
                    logger.LogInformation(
                        "{File}: {Copied:N0} байт, {Speed:F1} MB/s",
                        fileName, copied, speedMBs);
                }

                lastLoggedBytes = copied;
                lastLoggedAt = now;
            }
        }
    }
}
