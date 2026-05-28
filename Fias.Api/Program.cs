using System.Threading.RateLimiting;
using Fias.Api.Auth;
using Fias.Application;
using Fias.Infrastructure;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// --- API-key authentication + Authorization (Public/Admin роли) ---
builder.Services.Configure<ApiKeyOptions>(builder.Configuration.GetSection(ApiKeyOptions.SectionName));

builder.Services
    .AddAuthentication(ApiKeyAuthenticationSchemeOptions.Scheme)
    .AddScheme<ApiKeyAuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationSchemeOptions.Scheme, _ => { });

builder.Services.AddAuthorization(opt =>
{
    opt.FallbackPolicy = new AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes(ApiKeyAuthenticationSchemeOptions.Scheme)
        .RequireAuthenticatedUser()
        .Build();
});

// --- Rate limiting: per API-key partition (или per IP, если ключа нет) ---
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var key = ctx.Request.Headers.TryGetValue(ApiKeyAuthenticationSchemeOptions.HeaderName, out var v)
            ? v.ToString()
            : ctx.Connection.RemoteIpAddress?.ToString() ?? "anon";
        // 600 запросов/мин с ключом, 60/мин без ключа.
        var permits = string.IsNullOrEmpty(key) || key == "anon" || ctx.Connection.RemoteIpAddress is not null && key == ctx.Connection.RemoteIpAddress.ToString()
            ? 60
            : 600;
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permits,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

// --- ProblemDetails (RFC 7807) для всех unhandled ошибок ---
builder.Services.AddProblemDetails(opt =>
{
    opt.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
    };
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Fias API",
        Version = "v1",
        Description = "REST API над Государственным Адресным Реестром (ГАР). Заголовок X-API-Key для авторизации."
    });
    c.AddSecurityDefinition(ApiKeyAuthenticationSchemeOptions.Scheme, new OpenApiSecurityScheme
    {
        Description = "API-key в заголовке X-API-Key",
        In = ParameterLocation.Header,
        Name = ApiKeyAuthenticationSchemeOptions.HeaderName,
        Type = SecuritySchemeType.ApiKey
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = ApiKeyAuthenticationSchemeOptions.Scheme
                }
            }
        ] = Array.Empty<string>()
    });
    var xmlFile = Path.Combine(AppContext.BaseDirectory, $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xmlFile))
        c.IncludeXmlComments(xmlFile);
});

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration);

builder.Services.AddHangfire(cfg =>
{
    cfg.SetDataCompatibilityLevel(CompatibilityLevel.Version_180);
    cfg.UseSimpleAssemblyNameTypeSerializer();
    cfg.UseRecommendedSerializerSettings();
    cfg.UsePostgreSqlStorage(opt =>
    {
        opt.UseNpgsqlConnection(builder.Configuration.GetConnectionString("Default"));
    });
});

// Намеренно не вызываем AddHangfireServer — обработка job'ов живёт в Fias.Service.Updater.
// В Api Hangfire нужен для Dashboard и для IBackgroundJobClient (AdminController enqueue'ит туда же).

builder.Services.AddSingleton<HangfireDashboardAuthorizationFilter>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseCors();
app.UseRateLimiter();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [app.Services.GetRequiredService<HangfireDashboardAuthorizationFilter>()],
    AppPath = "/",
    DashboardTitle = "ФИАС — задачи обновления"
});

app.Run();
