using Fias.Api.Auth;
using Fias.Application.Models;
using Fias.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fias.Api.Controllers;

[ApiController]
[Authorize(Roles = ApiKeyRoles.Admin)]
[Route("api/v1/admin/imports")]
[Produces("application/json")]
public class AdminController(IAdminImportService imports) : ControllerBase
{
    /// <summary>
    /// Поставить full-data-import в очередь Hangfire. Если <c>localZipPath</c> не задан —
    /// воркер сам решит: брать gar_xml.zip из ImportDirectory или скачать с ФНС.
    /// </summary>
    [HttpPost("full")]
    [ProducesResponseType(typeof(AdminImportResponse), StatusCodes.Status202Accepted)]
    public ActionResult<AdminImportResponse> EnqueueFull([FromBody] AdminImportRequest? request)
    {
        var response = imports.EnqueueFull(request ?? new AdminImportRequest(null));
        return Accepted(response);
    }

    /// <summary>Поставить дельта-импорт в очередь вне расписания.</summary>
    [HttpPost("delta")]
    [ProducesResponseType(typeof(AdminImportResponse), StatusCodes.Status202Accepted)]
    public ActionResult<AdminImportResponse> EnqueueDelta()
    {
        var response = imports.EnqueueDelta();
        return Accepted(response);
    }
}
