using Fias.Service.Updater;
using Hangfire;
using Hangfire.PostgreSql;

var builder = Host.CreateApplicationBuilder(args);

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

builder.Services.AddHangfireServer(opt =>
{
    opt.WorkerCount = 2;
    opt.Queues = ["fias"];
});
       
builder.Services.AddHostedService<Worker>();

var host = builder.Build();

host.Run();