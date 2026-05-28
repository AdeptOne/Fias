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
builder.Services.AddSingleton<IFiasArchiveReader, FiasArchiveReader>();
// Orchestrator зависит от typed-HttpClient'ов (Transient) — он сам должен быть Scoped,
// иначе DI поймает captive dependency при validateScopes=true.
builder.Services.AddScoped<IMigrator, Migrator>();
builder.Services.AddScoped<IFiasVersionStore, FiasVersionStore>();
builder.Services.AddScoped<IFiasImportOrchestrator, FiasImportOrchestrator>();
builder.Services.AddScoped<FiasUpdateJob>();
builder.Services.AddScoped<Fias.Application.Services.IFiasUpdateJob>(sp => sp.GetRequiredService<FiasUpdateJob>());

// Импортёры базового набора (см. «Правила формирования адресной строки»).
builder.Services.AddSingleton<IFiasEntityImporter, ReestrObjectImporter>();
builder.Services.AddSingleton<IFiasEntityImporter, AddressObjectImporter>();
builder.Services.AddSingleton<IFiasEntityImporter, HouseImporter>();
builder.Services.AddSingleton<IFiasEntityImporter, ApartmentImporter>();
builder.Services.AddSingleton<IFiasEntityImporter, RoomImporter>();
builder.Services.AddSingleton<IFiasEntityImporter, MunHierarchyImporter>();
builder.Services.AddSingleton<IFiasEntityImporter, AdmHierarchyImporter>();
builder.Services.AddSingleton<IFiasEntityImporter, AddressObjectTypeImporter>();
builder.Services.AddSingleton<IFiasEntityImporter, HouseTypeImporter>();
builder.Services.AddSingleton<IFiasEntityImporter, ApartmentTypeImporter>();
builder.Services.AddSingleton<IFiasEntityImporter, RoomTypeImporter>();
builder.Services.AddSingleton<IFiasEntityImporter, ObjectLevelImporter>();
builder.Services.AddSingleton<IFiasEntityImporter, ParamImporter>();
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
