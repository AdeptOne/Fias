using Hangfire;

namespace Fias.Application.Services;

/// <summary>
/// Контракт Hangfire job'а обновления ФИАС. Реализация живёт в Fias.Service.Updater;
/// Infrastructure использует интерфейс, чтобы enqueue не зависел от worker-проекта.
/// Атрибут <see cref="QueueAttribute"/> здесь критичен — Hangfire берёт очередь из MethodInfo
/// интерфейса при enqueue через интерфейс, а также при retry и schedule. Без него
/// неудавшиеся задачи попадают в очередь default.
/// </summary>
public interface IFiasUpdateJob
{
    [Queue(FiasJobQueues.Fias)]
    Task RunFullAsync(string? localZipPath, CancellationToken ct);

    [Queue(FiasJobQueues.Fias)]
    Task RunDeltaAsync(CancellationToken ct);
}

public static class FiasJobQueues
{
    public const string Fias = "fias";
}
