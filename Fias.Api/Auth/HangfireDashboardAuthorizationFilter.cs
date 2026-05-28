using Hangfire.Dashboard;
using Microsoft.Extensions.Options;

namespace Fias.Api.Auth;

/// <summary>
/// Доступ к Hangfire Dashboard по одному root-токену.
/// Токен задаётся в конфиге (Hangfire:RootToken). Браузер передаёт его при первом визите
/// через query string (?token=...), фильтр сохраняет его в cookie hf_token и пропускает.
/// </summary>
public class HangfireDashboardAuthorizationFilter(IOptions<HangfireDashboardOptions> options)
    : IDashboardAuthorizationFilter
{
    private const string CookieName = "hf_token";
    private const string QueryParamName = "token";

    public bool Authorize(DashboardContext context)
    {
        var expected = options.Value.RootToken;
        if (string.IsNullOrEmpty(expected))
            return false;

        var httpContext = context.GetHttpContext();

        if (httpContext.Request.Cookies.TryGetValue(CookieName, out var cookie)
            && string.Equals(cookie, expected, StringComparison.Ordinal))
        {
            return true;
        }

        if (httpContext.Request.Query.TryGetValue(QueryParamName, out var provided)
            && string.Equals(provided, expected, StringComparison.Ordinal))
        {
            httpContext.Response.Cookies.Append(CookieName, expected, new CookieOptions
            {
                HttpOnly = true,
                Secure = httpContext.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddDays(7)
            });
            return true;
        }

        return false;
    }
}
