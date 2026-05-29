using Fias.Application.Models;
using Fias.Application.Services;
using Hangfire;
using Hangfire.States;

namespace Fias.Infrastructure.Hangfire;

/// <summary>
/// Постановка ad-hoc job'ов ФИАС через интерфейс <see cref="IFiasUpdateJob"/>.
/// Конкретный класс job'а живёт в Fias.Service.Updater и подхватывается Hangfire активатором
/// при выполнении — Api/Infrastructure не нуждаются в reference на worker-проект.
/// </summary>
public class AdminImportService(IBackgroundJobClient jobs) : IAdminImportService
{
    public AdminImportResponse EnqueueFull(AdminImportRequest request)
    {
        var localZipPath = string.IsNullOrWhiteSpace(request.LocalZipPath) ? null : request.LocalZipPath.Trim();
        var jobId = jobs.Create(
            global::Hangfire.Common.Job.FromExpression<IFiasUpdateJob>(
                j => j.RunFullAsync(localZipPath, CancellationToken.None)),
            new EnqueuedState(FiasJobQueues.Fias));
        return new AdminImportResponse(jobId);
    }

    public AdminImportResponse EnqueueDelta()
    {
        var jobId = jobs.Create(
            global::Hangfire.Common.Job.FromExpression<IFiasUpdateJob>(
                j => j.RunDeltaAsync(CancellationToken.None)),
            new EnqueuedState(FiasJobQueues.Fias));
        return new AdminImportResponse(jobId);
    }
}
