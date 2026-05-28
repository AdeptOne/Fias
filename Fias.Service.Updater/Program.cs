using Fias.Service.Updater;
using Fias.Service.Updater.Jobs;
using Fias.Service.Updater.Options;
using Fias.Service.Updater.Services;
using Fias.Service.Updater.Services.Archives;
using Fias.Service.Updater.Services.Db;
using Fias.Service.Updater.Services.Downloading;
using Fias.Service.Updater.Services.Importing;
using Fias.Service.Updater.Services.Schema;
using Fias.Service.Updater.Services.State;
using Hangfire;
using Hangfire.PostgreSql;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<FiasOptions>(builder.Configuration.GetSection(FiasOptions.SectionName));

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

builder.Services.AddSingleton<INpgsqlConnectionFactory, NpgsqlConnectionFactory>();
builder.Services.AddSingleton<IMigrator, Migrator>();
builder.Services.AddSingleton<IFiasVersionStore, FiasVersionStore>();
builder.Services.AddSingleton<IFiasArchiveReader, FiasArchiveReader>();
builder.Services.AddSingleton<IFiasImportOrchestrator, FiasImportOrchestrator>();
builder.Services.AddScoped<FiasUpdateJob>();

// Импортёры. Сейчас полностью реализованы только два — остальные сущности из архива
// будут безопасно пропущены оркестратором до их реализации (см. CopyImporterBase).
builder.Services.AddSingleton<IFiasEntityImporter, AddressObjectImporter>();
builder.Services.AddSingleton<IFiasEntityImporter, AddressObjectTypeImporter>();
builder.Services.AddSingleton<IFiasEntityImporterRegistry, FiasEntityImporterRegistry>();

builder.Services.AddHttpClient<IFiasFnsClient, FiasFnsClient>(c =>
{
    c.Timeout = TimeSpan.FromMinutes(2);
});
builder.Services.AddHttpClient<IFiasDownloader, FiasDownloader>(c =>
{
    c.Timeout = TimeSpan.FromHours(2);
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
