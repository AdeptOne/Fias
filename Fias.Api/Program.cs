using Fias.Api.Auth;
using Fias.Application;
using Fias.Infrastructure;
using Hangfire;
using Hangfire.PostgreSql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Fias API", Version = "v1" });
    var xmlFile = Path.Combine(AppContext.BaseDirectory, $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xmlFile))
        c.IncludeXmlComments(xmlFile);
});

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
// Здесь Hangfire нужен только для Dashboard поверх общего PostgreSQL-стораджа.

builder.Services.AddSingleton<HangfireDashboardAuthorizationFilter>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [app.Services.GetRequiredService<HangfireDashboardAuthorizationFilter>()],
    AppPath = "/",
    DashboardTitle = "ФИАС — задачи обновления"
});

app.Run();
