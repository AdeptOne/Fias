namespace Fias.Application.Services;

/// <summary>
/// Контракт Hangfire job'а обновления ФИАС. Реализация живёт в Fias.Service.Updater;
/// Infrastructure использует интерфейс, чтобы enqueue не зависел от worker-проекта.
/// </summary>
public interface IFiasUpdateJob
{
    Task RunFullAsync(string? localZipPath, CancellationToken ct);
    Task RunDeltaAsync(CancellationToken ct);
}
