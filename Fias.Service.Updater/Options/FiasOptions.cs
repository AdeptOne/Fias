namespace Fias.Service.Updater.Options;

public class FiasOptions
{
    public const string SectionName = "Fias";

    public string FnsServiceUrl { get; set; } = "https://fias.nalog.ru/WebServices/Public";

    public string ActualDownloadsBaseUrl { get; set; } = "https://fias.nalog.ru/Public/Downloads/Actual";

    /// <summary>Папка, куда монтируется volume с локальными ZIP-файлами и куда складываются скачанные.</summary>
    public string ImportDirectory { get; set; } = "/fias/import";

    /// <summary>Имя файла полной выгрузки в ImportDirectory, используется по умолчанию для full-data-import.</summary>
    public string FullArchiveFileName { get; set; } = "gar_xml.zip";

    /// <summary>Имя файла дельта-выгрузки, скачивается каждый день.</summary>
    public string DeltaArchiveFileName { get; set; } = "gar_delta_xml.zip";

    /// <summary>Размер батча для bulk-импорта (COPY).</summary>
    public int CopyBatchSize { get; set; } = 5000;

    /// <summary>Список регионов для импорта (двузначные коды). Пусто — все регионы.</summary>
    public string[] Regions { get; set; } = [];
}
