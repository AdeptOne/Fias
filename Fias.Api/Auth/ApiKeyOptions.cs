namespace Fias.Api.Auth;

public class ApiKeyOptions
{
    public const string SectionName = "ApiKeys";

    public List<ApiKey> Keys { get; set; } = new();
}

public class ApiKey
{
    public string Key { get; set; } = string.Empty;
    public string Role { get; set; } = ApiKeyRoles.Public;
    public string? Owner { get; set; }
}

public static class ApiKeyRoles
{
    public const string Public = "Public";
    public const string Admin = "Admin";
}
