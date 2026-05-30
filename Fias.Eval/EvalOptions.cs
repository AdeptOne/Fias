using System.Globalization;

namespace Fias.Eval;

/// <summary>
/// Параметры прогона eval. Разбираются из argv в форме <c>--key value</c> / <c>--flag</c>.
/// Все значения имеют разумные дефолты, чтобы прогон запускался без аргументов.
/// </summary>
public sealed record EvalOptions
{
    /// <summary>Явный список region_object_id (корень субъекта в path). Пусто — авто-выбор топ-N по объёму.
    /// Примечание: region_code в проекции часто пуст (нет params typeid=12), поэтому партиционируем по
    /// region_object_id — он заполнен всегда.</summary>
    public IReadOnlyList<long> Regions { get; init; } = [];

    /// <summary>Сколько регионов взять авто-выбором (топ по числу улиц), если <see cref="Regions"/> пуст.</summary>
    public int RegionCount { get; init; } = 5;

    /// <summary>Сколько улиц семплировать на регион (каждая даёт несколько запросов-вариантов).</summary>
    public int StreetsPerRegion { get; init; } = 40;

    /// <summary>Сколько домов семплировать на регион (бакет house).</summary>
    public int HousesPerRegion { get; init; } = 20;

    /// <summary>Сколько объектов type-confusion семплировать на регион (бакет type).</summary>
    public int TypeDupsPerRegion { get; init; } = 20;

    /// <summary>Сид для воспроизводимой выборки (md5(object_id || seed)). Один сид = один и тот же набор.</summary>
    public string Seed { get; init; } = "fias-eval-1";

    /// <summary>Глубина top-k, запрашиваемая у поиска (и максимальная для recall@k).</summary>
    public int Limit { get; init; } = 10;

    /// <summary>Куда писать CSV с промахами (запрос, ожидаемое, что вернулось). Пусто — не писать.</summary>
    public string MissesCsv { get; init; } = "eval-misses.csv";

    public static EvalOptions Parse(string[] args)
    {
        var o = new EvalOptions();
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Нет значения для {args[i]}");
            int NextInt() => int.Parse(Next(), CultureInfo.InvariantCulture);

            o = args[i] switch
            {
                "--regions" => o with { Regions = Next().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(long.Parse).ToArray() },
                "--region-count" => o with { RegionCount = NextInt() },
                "--streets" => o with { StreetsPerRegion = NextInt() },
                "--houses" => o with { HousesPerRegion = NextInt() },
                "--type-dups" => o with { TypeDupsPerRegion = NextInt() },
                "--seed" => o with { Seed = Next() },
                "--limit" => o with { Limit = NextInt() },
                "--out" => o with { MissesCsv = Next() },
                "--no-csv" => o with { MissesCsv = string.Empty },
                _ => throw new ArgumentException($"Неизвестный аргумент: {args[i]}"),
            };
        }
        return o;
    }
}
