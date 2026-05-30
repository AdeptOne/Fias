using System.Collections.Frozen;
using Dapper;
using Npgsql;

namespace Fias.Eval;

/// <summary>
/// Полу-автоматическая генерация золотого набора прямо из проекции search.*: каждая её строка
/// уже несёт эталонный <c>object_guid</c>, регион и денормализованные город/улица/тип — значит
/// запрос можно синтезировать, а правильный ответ известен без ручной разметки.
///
/// Выборка детерминирована: порядок задаётся <c>md5(object_id || seed)</c>, поэтому при одном
/// сиде набор воспроизводится один-в-один (нужно для регрессии качества между прогонами).
/// </summary>
public sealed class GoldenSetGenerator(NpgsqlDataSource dataSource)
{
    // Сокращение типа из ГАР (type_name) → слово, которое реально печатает пользователь.
    // Нужен только для бакета type-confusion: подставляем тип в запрос, хотя нормализатор его
    // отбрасывает — так проверяем, разводит ли ранжирование «ул» и «пр-кт» при равном имени.
    private static readonly FrozenDictionary<string, string> TypeWord =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ул"] = "улица", ["пер"] = "переулок", ["пр-кт"] = "проспект", ["пр-т"] = "проспект",
            ["ш"] = "шоссе", ["б-р"] = "бульвар", ["пл"] = "площадь", ["наб"] = "набережная",
            ["проезд"] = "проезд", ["туп"] = "тупик", ["аллея"] = "аллея", ["мкр"] = "микрорайон",
            ["кв-л"] = "квартал", ["линия"] = "линия", ["тракт"] = "тракт",
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<GoldenItem>> GenerateAsync(EvalOptions opt, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        var regions = opt.Regions.Count > 0
            ? opt.Regions
            : await PickRegionsAsync(conn, opt.RegionCount, ct);

        var items = new List<GoldenItem>();
        foreach (var region in regions)
        {
            items.AddRange(await StreetItemsAsync(conn, region, opt, ct));
            items.AddRange(await HouseItemsAsync(conn, region, opt, ct));
            items.AddRange(await TypeConfusionItemsAsync(conn, region, opt, ct));
        }
        return items;
    }

    /// <summary>Топ-N субъектов (по region_object_id) по числу улиц — адаптируемся к тому, что импортировано.</summary>
    private static async Task<IReadOnlyList<long>> PickRegionsAsync(NpgsqlConnection conn, int count, CancellationToken ct)
    {
        const string sql = """
            SELECT region_object_id
            FROM search.address_objects
            WHERE region_object_id IS NOT NULL AND level = 8
            GROUP BY region_object_id
            ORDER BY count(*) DESC
            LIMIT @count
            """;
        var rows = await conn.QueryAsync<long>(new CommandDefinition(sql, new { count }, cancellationToken: ct));
        return rows.AsList();
    }

    private sealed record StreetRow(Guid Guid, string Container, string Name, string? TypeName);

    /// <summary>Улицы с домами (живые) → бакеты clean/reorder/typo/abbrev на каждую.</summary>
    private async Task<IEnumerable<GoldenItem>> StreetItemsAsync(
        NpgsqlConnection conn, long region, EvalOptions opt, CancellationToken ct)
    {
        const string sql = """
            SELECT object_guid              AS "Guid",
                   coalesce(city, settlement) AS "Container",
                   name                     AS "Name",
                   type_name                AS "TypeName"
            FROM search.address_objects
            WHERE level = 8
              AND object_guid IS NOT NULL
              AND name IS NOT NULL
              AND coalesce(city, settlement) IS NOT NULL
              AND house_count > 0
              AND region_object_id = @region
            ORDER BY md5(object_id::text || @seed)
            LIMIT @take
            """;
        var rows = await conn.QueryAsync<StreetRow>(new CommandDefinition(
            sql, new { region, seed = opt.Seed, take = opt.StreetsPerRegion }, cancellationToken: ct));

        var items = new List<GoldenItem>();
        foreach (var r in rows)
        {
            var label = $"{r.Container}, {r.TypeName} {r.Name}".Trim();
            items.Add(new GoldenItem("clean", $"{r.Container} {r.Name}", r.Guid, label));
            items.Add(new GoldenItem("reorder", $"{r.Name} {r.Container}", r.Guid, label));

            var typo = Typo(r.Name);
            if (typo != r.Name)
                items.Add(new GoldenItem("typo", $"{r.Container} {typo}", r.Guid, label));

            // abbrev: подставляем сокращённый тип улицы (его нормализатор раскроет/отбросит).
            if (r.TypeName is { Length: > 0 })
                items.Add(new GoldenItem("abbrev", $"{r.Container} {r.TypeName} {r.Name}", r.Guid, label));
        }
        return items;
    }

    private sealed record HouseRow(Guid Guid, string Container, string Street, string HouseNum);

    /// <summary>Дома под улицами → бакет house: «город улица номер».</summary>
    private static async Task<IEnumerable<GoldenItem>> HouseItemsAsync(
        NpgsqlConnection conn, long region, EvalOptions opt, CancellationToken ct)
    {
        const string sql = """
            SELECT h.object_guid              AS "Guid",
                   coalesce(a.city, a.settlement) AS "Container",
                   a.name                     AS "Street",
                   h.house_num                AS "HouseNum"
            FROM search.houses h
            JOIN search.address_objects a ON a.object_id = h.parent_object_id
            WHERE a.level = 8
              AND h.object_guid IS NOT NULL
              AND a.name IS NOT NULL
              AND coalesce(a.city, a.settlement) IS NOT NULL
              AND h.house_num ~ '^[0-9]+$'
              AND a.region_object_id = @region
            ORDER BY md5(h.object_id::text || @seed)
            LIMIT @take
            """;
        var rows = await conn.QueryAsync<HouseRow>(new CommandDefinition(
            sql, new { region, seed = opt.Seed, take = opt.HousesPerRegion }, cancellationToken: ct));

        // Два бакета на дом: с городом (house) и без города (nocity — стресс «улица+дом» без
        // контейнера: одноимённые улицы по городам, точный номер должен всплыть в нескольких).
        return rows.SelectMany(r => new[]
        {
            new GoldenItem("house", $"{r.Container} {r.Street} {r.HouseNum}", r.Guid,
                $"{r.Container}, {r.Street}, д {r.HouseNum}"),
            new GoldenItem("nocity", $"{r.Street} {r.HouseNum}", r.Guid,
                $"{r.Container}, {r.Street}, д {r.HouseNum}"),
        }).ToList();
    }

    private sealed record TypeRow(Guid Guid, string Container, string Name, string TypeName);

    /// <summary>
    /// Стресс-бакет type-confusion: имена улиц, встречающиеся в одном городе с РАЗНЫМИ типами
    /// (напр. «Ленина» как ул и как пр-кт). Запрос несёт конкретный тип, эталон — его вариант.
    /// Если на этом бакете recall просядет — это и есть аргумент за бонус к type_name.
    /// </summary>
    private static async Task<IEnumerable<GoldenItem>> TypeConfusionItemsAsync(
        NpgsqlConnection conn, long region, EvalOptions opt, CancellationToken ct)
    {
        const string sql = """
            WITH dup AS (
                SELECT region_object_id AS rc, coalesce(city, settlement) AS container, name AS nm
                FROM search.address_objects
                WHERE level = 8
                  AND name IS NOT NULL
                  AND coalesce(city, settlement) IS NOT NULL
                  AND type_name IS NOT NULL
                  AND region_object_id = @region
                GROUP BY region_object_id, coalesce(city, settlement), name
                HAVING count(DISTINCT type_name) >= 2
            )
            SELECT a.object_guid              AS "Guid",
                   coalesce(a.city, a.settlement) AS "Container",
                   a.name                     AS "Name",
                   a.type_name                AS "TypeName"
            FROM search.address_objects a
            JOIN dup ON dup.rc = a.region_object_id
                    AND dup.container = coalesce(a.city, a.settlement)
                    AND dup.nm = a.name
            WHERE a.level = 8
              AND a.object_guid IS NOT NULL
              AND a.type_name IS NOT NULL
            ORDER BY md5(a.object_id::text || @seed)
            LIMIT @take
            """;
        var rows = await conn.QueryAsync<TypeRow>(new CommandDefinition(
            sql, new { region, seed = opt.Seed, take = opt.TypeDupsPerRegion }, cancellationToken: ct));

        var items = new List<GoldenItem>();
        foreach (var r in rows)
        {
            if (!TypeWord.TryGetValue(r.TypeName, out var word)) continue; // тип без узнаваемого слова — пропускаем
            var label = $"{r.Container}, {r.TypeName} {r.Name}";
            items.Add(new GoldenItem("type", $"{r.Container} {word} {r.Name}", r.Guid, label));
        }
        return items;
    }

    /// <summary>
    /// Детерминированная опечатка: транспозиция двух соседних букв ближе к середине слова
    /// (реалистичная ошибка набора, правка расстояния ≤ 2). Для коротких имён — без изменений.
    ///
    /// Мутируем ТОЛЬКО чисто-буквенные слова (≥ 4 букв): в числовых именах («12-я», «4-я»)
    /// транспозиция цифр/дефиса порождает ДРУГУЮ реальную улицу («1-2я» → «1-я …»), а не опечатку —
    /// это искажало бакет typo ложными «промахами».
    /// </summary>
    private static string Typo(string name)
    {
        var words = name.Split(' ');
        for (var w = 0; w < words.Length; w++)
        {
            var s = words[w];
            if (s.Length < 4 || !s.All(char.IsLetter)) continue;

            var chars = s.ToCharArray();
            var mid = chars.Length / 2;
            // Ближайшая к середине пара соседних РАЗНЫХ букв (одинаковые дали бы то же слово).
            for (var d = 0; d < chars.Length; d++)
            {
                foreach (var i in new[] { mid + d, mid - d })
                {
                    if (i < 1 || i >= chars.Length || chars[i - 1] == chars[i]) continue;
                    (chars[i - 1], chars[i]) = (chars[i], chars[i - 1]);
                    words[w] = new string(chars);
                    return string.Join(' ', words);
                }
            }
        }
        return name;
    }
}
