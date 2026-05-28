using Fias.Application.Models;

namespace Fias.Application.Services;

public interface IAdminImportService
{
    /// <summary>Ставит full-data-import в очередь Hangfire, возвращает идентификатор job'а.</summary>
    AdminImportResponse EnqueueFull(AdminImportRequest request);

    /// <summary>Ставит delta-data-import в очередь Hangfire.</summary>
    AdminImportResponse EnqueueDelta();
}
