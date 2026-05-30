using Fias.Application;
using Fias.Application.Abstractions;
using Fias.Eval;
using Fias.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

// Харнесс качества поиска: генерирует золотой набор из самой проекции search.* (эталон = object_guid),
// гоняет его через боевой IAddressSearchService и печатает recall@k / MRR по бакетам.
//
// Запуск:  dotnet run --project Fias.Eval -- [--regions 54,77] [--region-count 5]
//          [--streets 40] [--houses 20] [--type-dups 20] [--seed s] [--limit 10] [--out misses.csv]
// Строка подключения берётся из appsettings.json или ConnectionStrings__Default (env).

EvalOptions opt;
try
{
    opt = EvalOptions.Parse(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Аргументы: {ex.Message}");
    return 2;
}

// ContentRoot = каталог бинаря: иначе `dotnet run` ищет appsettings.json в CWD вызова, а не рядом с dll.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = AppContext.BaseDirectory });

// Регистрируем ТОЛЬКО то, что нужно поиску — без AddInfrastructure целиком: тот тянет
// Hangfire-обвязку (AdminImportService → IBackgroundJobClient), которой в eval нет, и хост
// в Development валит контейнер на Build() при её валидации.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default не задан");
builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
builder.Services.AddScoped<ISqlConnectionFactory, SqlConnectionFactory>();
builder.Services.AddScoped<IAddressSearchRepository, AddressSearchRepository>();
builder.Services.AddApplication();
builder.Services.AddSingleton<GoldenSetGenerator>();
using var host = builder.Build();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
var ct = cts.Token;

await using var scope = host.Services.CreateAsyncScope();
var sp = scope.ServiceProvider;

// Быстрая проверка связи и наличия проекции — чтобы упасть понятно, а не на первом запросе.
var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
try
{
    await using var conn = await dataSource.OpenConnectionAsync(ct);
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT count(*) FROM search.address_objects WHERE level = 8";
    var streets = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    Console.WriteLine($"Проекция search.address_objects: улиц (level=8) — {streets:N0}");
    if (streets == 0)
    {
        Console.Error.WriteLine("Проекция пуста — сначала импорт + SearchProjectionBuilder.RebuildAsync.");
        return 1;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Нет связи с БД / проекцией: {ex.Message}");
    return 1;
}

var generator = sp.GetRequiredService<GoldenSetGenerator>();
Console.WriteLine("Генерация золотого набора из search.*…");
var golden = await generator.GenerateAsync(opt, ct);
Console.WriteLine($"Сгенерировано запросов: {golden.Count} (сид «{opt.Seed}»).");
if (golden.Count == 0)
{
    Console.Error.WriteLine("Набор пуст — проверьте регионы/наличие улиц с домами в проекции.");
    return 1;
}

var runner = new EvalRunner(sp.GetRequiredService<Fias.Application.Services.IAddressSearchService>());
Console.WriteLine("Прогон запросов через IAddressSearchService…");
var report = await runner.RunAsync(golden, opt.Limit, ct);

Console.WriteLine(EvalRunner.FormatTable(report));

if (!string.IsNullOrEmpty(opt.MissesCsv) && report.Misses.Count > 0)
{
    await File.WriteAllTextAsync(opt.MissesCsv, EvalRunner.FormatMissesCsv(report.Misses), ct);
    Console.WriteLine($"Промахов (рекалл-мисс или эталон не на #1): {report.Misses.Count} → {opt.MissesCsv}");
}

return 0;
