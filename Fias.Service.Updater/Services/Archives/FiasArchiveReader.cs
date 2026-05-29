using System.IO.Compression;
using Fias.Service.Updater.Options;
using Microsoft.Extensions.Options;

namespace Fias.Service.Updater.Services.Archives;

/// <summary>Метаданные одного файла внутри ZIP (без открытого стрима — стрим даёт IArchiveAccessor).</summary>
public record FiasArchiveEntry(
    FiasEntityKind Kind,
    string FullName,
    string? RegionCode);

/// <summary>
/// Доступ к содержимому конкретного архива. НЕ потокобезопасен: один аксессор = один открытый
/// ZipArchive, рассчитан на использование ровно одним воркером (ZipArchive нельзя читать из
/// нескольких потоков одновременно).
/// </summary>
public interface IArchiveAccessor : IDisposable
{
    /// <summary>Открыть распакованный поток файла по его FullName.</summary>
    Stream Open(string fullName);
}

public interface IFiasArchiveReader
{
    /// <summary>Перечислить подходящие файлы архива (метаданные). Архив при этом закрывается.</summary>
    IReadOnlyList<FiasArchiveEntry> Enumerate(string zipPath);

    /// <summary>Открыть отдельный аксессор к архиву (по одному на параллельный воркер).</summary>
    IArchiveAccessor OpenAccessor(string zipPath);
}

public class FiasArchiveReader(IOptions<FiasOptions> options) : IFiasArchiveReader
{
    private readonly FiasOptions _options = options.Value;

    public IReadOnlyList<FiasArchiveEntry> Enumerate(string zipPath)
    {
        var allowedRegions = _options.Regions.Length == 0
            ? null
            : new HashSet<string>(_options.Regions, StringComparer.Ordinal);

        // Архив открываем только для чтения метаданных (central directory) и сразу закрываем.
        // Стримы файлов потом открывает аксессор на своём экземпляре ZipArchive.
        using var archive = ZipFile.OpenRead(zipPath);
        var result = new List<FiasArchiveEntry>();

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

            result.Add(new FiasArchiveEntry(kind, entry.FullName, regionCode));
        }

        return result;
    }

    public IArchiveAccessor OpenAccessor(string zipPath) => new ZipArchiveAccessor(ZipFile.OpenRead(zipPath));

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

    private sealed class ZipArchiveAccessor(ZipArchive archive) : IArchiveAccessor
    {
        public Stream Open(string fullName)
        {
            var entry = archive.GetEntry(fullName)
                ?? throw new InvalidOperationException($"Файл '{fullName}' не найден в архиве");
            return entry.Open();
        }

        public void Dispose() => archive.Dispose();
    }
}
