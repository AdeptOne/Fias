namespace Fias.Api.Auth;

public class HangfireDashboardOptions
{
    public const string SectionName = "Hangfire";

    /// <summary>
    /// Секретный токен для доступа к /hangfire. Передаётся первым запросом как ?token=...,
    /// далее сохраняется в cookie hf_token. Пусто/null — дашборд недоступен никому.
    /// </summary>
    public string? RootToken { get; set; }
}
