using System.Net.Http.Json;
using Fias.Service.Updater.Options;
using Microsoft.Extensions.Options;

namespace Fias.Service.Updater.Services.Downloading;

public interface IFiasFnsClient
{
    Task<DownloadFileInfo> GetLastAsync(CancellationToken ct);
    Task<IReadOnlyList<DownloadFileInfo>> GetAllAsync(CancellationToken ct);
}

public class FiasFnsClient(HttpClient http, IOptions<FiasOptions> options, ILogger<FiasFnsClient> logger) : IFiasFnsClient
{
    private readonly FiasOptions _options = options.Value;

    public async Task<DownloadFileInfo> GetLastAsync(CancellationToken ct)
    {
        var url = $"{_options.FnsServiceUrl.TrimEnd('/')}/GetLastDownloadFileInfo";
        logger.LogInformation("Запрос последней версии ФИАС: {Url}", url);

        var info = await http.GetFromJsonAsync<DownloadFileInfo>(url, ct)
            ?? throw new InvalidOperationException("ФНС вернул пустой ответ на GetLastDownloadFileInfo");

        logger.LogInformation("Получена версия {VersionId} ({TextVersion})", info.VersionId, info.TextVersion);
        return info;
    }

    public async Task<IReadOnlyList<DownloadFileInfo>> GetAllAsync(CancellationToken ct)
    {
        var url = $"{_options.FnsServiceUrl.TrimEnd('/')}/GetAllDownloadFileInfo";
        logger.LogInformation("Запрос всех версий ФИАС: {Url}", url);

        var list = await http.GetFromJsonAsync<List<DownloadFileInfo>>(url, ct)
            ?? throw new InvalidOperationException("ФНС вернул пустой ответ на GetAllDownloadFileInfo");

        return list;
    }
}
