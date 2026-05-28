using Fias.Service.Updater.Services.Archives;

namespace Fias.Service.Updater.Services.Importing;

public enum ImportMode
{
    /// <summary>Полная загрузка — таблица предварительно очищается (TRUNCATE) и в неё льётся COPY.</summary>
    Full,

    /// <summary>Инкрементальный апдейт — записи UPSERT'ятся через временную staging-таблицу.</summary>
    Delta
}

public interface IFiasEntityImporter
{
    FiasEntityKind Kind { get; }

    /// <summary>
    /// Прочитать XML-поток и применить его к БД согласно режиму.
    /// </summary>
    /// <returns>Число обработанных записей.</returns>
    Task<long> ImportAsync(Stream xml, ImportMode mode, CancellationToken ct);
}
