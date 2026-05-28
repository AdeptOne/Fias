using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Fias.Api.Auth;

public class ApiKeyAuthenticationSchemeOptions : AuthenticationSchemeOptions
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-API-Key";
}

public class ApiKeyAuthenticationHandler(
    IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<ApiKeyOptions> apiKeys)
    : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationSchemeOptions.HeaderName, out var values))
            return Task.FromResult(AuthenticateResult.NoResult());

        var provided = values.ToString();
        if (string.IsNullOrEmpty(provided))
            return Task.FromResult(AuthenticateResult.NoResult());

        var match = apiKeys.Value.Keys.FirstOrDefault(k =>
            string.Equals(k.Key, provided, StringComparison.Ordinal));
        if (match is null)
            return Task.FromResult(AuthenticateResult.Fail("Неизвестный API-key"));

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, match.Owner ?? "api"),
            new(ClaimTypes.Role, match.Role)
        };
        var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationSchemeOptions.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationSchemeOptions.Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
