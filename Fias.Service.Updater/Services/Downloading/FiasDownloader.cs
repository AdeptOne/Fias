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

        await using (var src = await response.Content.ReadAsStreamAsync(ct))
        await using (var dst = File.Create(tempPath))
        {
            await src.CopyToAsync(dst, ct);
        }

        File.Move(tempPath, targetPath, overwrite: true);
        logger.LogInformation("Скачано: {Path} ({Length} байт)", targetPath, new FileInfo(targetPath).Length);
        return targetPath;
    }
}
