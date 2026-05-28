using Fias.Application.Models;

namespace Fias.Application.Abstractions;

/// <summary>
/// Текущая версия выгрузки ФИАС в БД. Реализуется в Infrastructure
/// (таблица fias.import_state, которую заполняет Updater).
/// </summary>
public interface IFiasVersionProvider
{
    Task<VersionDto> GetAsync(CancellationToken ct);
}
