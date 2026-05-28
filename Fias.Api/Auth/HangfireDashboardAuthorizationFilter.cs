using Hangfire.Dashboard;

namespace Fias.Api.Auth;

/// <summary>
/// Открывает Hangfire Dashboard всем в Development и только локальным запросам в остальных средах.
/// Для production стоит заменить на проверку ролей/токена через Hangfire.Dashboard.Authorization.
/// </summary>
public class HangfireDashboardAuthorizationFilter(IHostEnvironment env) : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        if (env.IsDevelopment())
            return true;

        var httpContext = context.GetHttpContext();
        var remote = httpContext.Connection.RemoteIpAddress;
        return remote is not null && System.Net.IPAddress.IsLoopback(remote);
    }
}
