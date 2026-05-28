using System.IO.Compression;
using Fias.Service.Updater.Options;
using Microsoft.Extensions.Options;

namespace Fias.Service.Updater.Services.Archives;

public record FiasArchiveEntry(
    FiasEntityKind Kind,
    string FullName,
    string? RegionCode,
    Func<Stream> OpenStream);

public interface IFiasArchiveReader
{
    IEnumerable<FiasArchiveEntry> Enumerate(string zipPath);
}

public class FiasArchiveReader(IOptions<FiasOptions> options) : IFiasArchiveReader
{
    private readonly FiasOptions _options = options.Value;

    public IEnumerable<FiasArchiveEntry> Enumerate(string zipPath)
    {
        var allowedRegions = _options.Regions.Length == 0
            ? null
            : new HashSet<string>(_options.Regions, StringComparer.Ordinal);

        // Один ZipArchive открывается и держится открытым на всё время итерации.
        // Стримы конкретных entry открываются вызывающим кодом по требованию через OpenStream().
        var archive = ZipFile.OpenRead(zipPath);
        try
        {
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;

                var kind = FiasEntityKindResolver.Resolve(entry.Name);
                if (kind == FiasEntityKind.Unknown)
                    continue;

                var regionCode = ExtractRegionCode(entry.FullName);
                if (allowedRegions is not null && regionCode is not null && !allowedRegions.Contains(regionCode))
                    continue;

                var capturedEntry = entry;
                yield return new FiasArchiveEntry(
                    kind,
                    entry.FullName,
                    regionCode,
                    () => capturedEntry.Open());
            }
        }
        finally
        {
            archive.Dispose();
        }
    }

    /// <summary>
    /// В gar_xml.zip пути выглядят как «01/AS_ADDR_OBJ_*.XML». Региональные справочники лежат в корне.
    /// </summary>
    private static string? ExtractRegionCode(string fullName)
    {
        var sep = fullName.IndexOf('/');
        if (sep <= 0) return null;
        var prefix = fullName[..sep];
        return prefix.Length is 2 or 3 && prefix.All(char.IsDigit) ? prefix : null;
    }
}
