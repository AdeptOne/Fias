using System.Text.Json.Serialization;

namespace Fias.Service.Updater.Services.Downloading;

/// <summary>
/// Ответ сервиса http://fias.nalog.ru/WebServices/Public — описание версии выгрузки.
/// </summary>
public class DownloadFileInfo
{
    [JsonPropertyName("VersionId")]
    public int VersionId { get; set; }

    [JsonPropertyName("TextVersion")]
    public string TextVersion { get; set; } = string.Empty;

    [JsonPropertyName("FiasCompleteDbfUrl")]
    public string? FiasCompleteDbfUrl { get; set; }

    [JsonPropertyName("FiasCompleteXmlUrl")]
    public string? FiasCompleteXmlUrl { get; set; }

    [JsonPropertyName("FiasDeltaDbfUrl")]
    public string? FiasDeltaDbfUrl { get; set; }

    [JsonPropertyName("FiasDeltaXmlUrl")]
    public string? FiasDeltaXmlUrl { get; set; }

    [JsonPropertyName("GarXMLFullURL")]
    public string? GarXmlFullUrl { get; set; }

    [JsonPropertyName("GarXMLDeltaURL")]
    public string? GarXmlDeltaUrl { get; set; }

    [JsonPropertyName("Date")]
    public string? Date { get; set; }
}
