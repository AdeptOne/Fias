using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Fias.Application.Search;

/// <summary>
/// Препроцессинг и нормализация пользовательской строки адреса.
///
/// Этапы: <c>Clean</c> (span, без лишних аллокаций) → <c>Tokenize+Expand</c> (раскрытие
/// сокращений через <see cref="FrozenDictionary{TKey,TValue}"/>) → эвристическое разбиение
/// на регион/город, улицу и дом. Класс stateless, все словари статические и неизменяемые,
/// поэтому регистрируется синглтоном.
///
/// Разбор намеренно «грубый» и быстрый: окончательное разрешение неоднозначностей делает
/// двухфазный SQL в репозитории (сужение по поддереву). Здесь — лишь дешёвая подсказка.
/// </summary>
public sealed partial class AddressNormalizer
{
    /// <summary>Сокращения и сленг → каноничное написание. FrozenDictionary — максимально
    /// быстрый доступ только на чтение (строится один раз, оптимизирован под lookup).</summary>
    private static readonly FrozenDictionary<string, string> Abbreviations =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // Сленг городов.
            ["нск"] = "новосибирск",
            ["спб"] = "санкт-петербург",
            ["мск"] = "москва",
            ["екб"] = "екатеринбург",
            ["нн"] = "нижний новгород",
            ["ннов"] = "нижний новгород",
            // Типовые сокращения (для FTS не критичны — типы всё равно отбрасываются,
            // но раскрытие делает Normalized человекочитаемым).
            ["ул"] = "улица",
            ["пр-т"] = "проспект",
            ["просп"] = "проспект",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Маркеры типов улиц — отбрасываются из «именной» части (тип не участвует в поиске).</summary>
    private static readonly FrozenSet<string> StreetMarkers = new[]
    {
        "улица", "ул", "переулок", "пер", "проспект", "пр-т", "просп", "площадь", "пл",
        "шоссе", "ш", "бульвар", "б-р", "набережная", "наб", "проезд", "тупик", "туп",
        "аллея", "линия", "микрорайон", "мкр", "квартал", "кв-л", "тракт", "кольцо",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Маркеры регионов/населённых пунктов — отбрасываются, но сигналят «это контейнер».</summary>
    private static readonly FrozenSet<string> RegionMarkers = new[]
    {
        "г", "гор", "город", "обл", "область", "край", "респ", "республика", "ао",
        "р-н", "район", "пос", "поселок", "рп", "пгт", "ст", "станица", "село",
        "снт", "тер", "территория", "нп",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>«Чистые» маркеры дома — слова без номера, выкидываются при нормализации номера.</summary>
    private static readonly FrozenSet<string> PureHouseMarkers = new[]
    {
        "д", "дом", "влд", "вл", "владение", "зд", "здание", "соор", "сооружение",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Маркеры корпуса/строения/литеры — после них идёт номер части дома.</summary>
    private static readonly FrozenSet<string> BuildingMarkers = new[]
    {
        "корпус", "корп", "к", "строение", "стр", "с", "литера", "лит", "л",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Токен-номер дома: опц. префикс (д/к/стр/...), цифры, опц. литера и опц.
    /// корпус/дробь («12», «12а», «3/1», «к1», «12к1»). Source-generated Regex компилируется
    /// на этапе сборки — без рантайм-затрат на разбор паттерна.</summary>
    [GeneratedRegex(
        @"^(?:дом|корпус|корп|строение|стр|владение|влд|вл|здание|зд|соор|лит|д|к)?\d+[а-яё]?(?:(?:[/-]|к|стр|корп|с)\d+[а-яё]?)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex HouseTokenRegex();

    /// <summary>Разбор одного домового токена на части: базовый номер, маркер корпуса и его номер.
    /// «12» → base; «12к1» → base+kind+bld; «к1» → kind+bld; «стр3» → kind+bld.</summary>
    // base: цифры, опц. дробь (3/1) и опц. литера — но литеру берём, только если за ней НЕ цифра
    // (иначе «12к1»: «к» — маркер корпуса, а не литера дома, и должна уйти в kind+bld).
    [GeneratedRegex(
        @"^(?<base>\d+(?:[/-]\d+)?(?:[а-яё](?![0-9]))?)?(?<kind>корп|стр|лит|к|с|л)?(?<bld>\d+[а-яё]?)?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex HousePartRegex();

    /// <summary>Главный метод: сырая строка → разобранный запрос.</summary>
    public ParsedAddressQuery Parse(string raw)
    {
        var normalized = Clean(raw);
        var tokens = TokenizeAndExpand(normalized);

        var (nameTokens, house) = SplitTrailingHouse(tokens);
        var (regionOrCity, street) = SplitContainerAndStreet(nameTokens);

        return new ParsedAddressQuery
        {
            Raw = raw,
            // Normalized пересобираем из раскрытых токенов — единая форма для логов и FTS.
            Normalized = string.Join(' ', tokens),
            Tokens = tokens,
            RegionOrCity = NullIfEmpty(regionOrCity),
            Street = NullIfEmpty(street),
            NameTokens = nameTokens,
            House = NullIfEmpty(house.Merged),
            HouseNum = house.Num,
            Building = house.Building,
        };
    }

    /// <summary>
    /// Альтернативные разборы для recall-фолбэка — когда основной дал пусто. От специфичного к общему;
    /// <see cref="Services.AddressSearchService"/> пробует их по очереди до первого непустого результата.
    /// </summary>
    public IEnumerable<ParsedAddressQuery> AlternativeSplits(ParsedAddressQuery query)
    {
        var nt = query.NameTokens;
        if (nt.Count == 0) yield break;

        var last = nt[^1];
        var head = nt.Take(nt.Count - 1).ToList();

        if (StreetMarkers.Contains(last))
        {
            var cont = NullIfEmpty(string.Join(' ', StripMarkers(head)));

            // 1) Тип-маркер как ИМЯ улицы, дом СОХРАНЯЕМ («Челябинск Тупик»; «город Линия 5» =
            //    дом 5 на ул. Линия). Пробуем первым — это более частая трактовка.
            if (cont is not null)
                yield return query with { RegionOrCity = cont, Street = last };

            // 2) Имя улицы = «<маркер> <число>» («Линия 2», «квартал 4»): число ошибочно ушло в дом.
            //    Только если дом-сохраняющий вариант не нашёлся → пересобираем имя, дом обнуляем.
            if (query.House is not null)
                yield return query with
                {
                    RegionOrCity = cont,
                    Street = $"{last} {query.HouseNum ?? query.House}",
                    House = null, HouseNum = null, Building = null,
                };
        }
        else if (nt.Count >= 2)
        {
            // Обратный порядок «улица … город» («Ленина Магнитогорск», «12-я Восточная Канашево»):
            // контейнер — последний токен, улица — остальное (без маркеров).
            var street = NullIfEmpty(string.Join(' ', StripMarkers(head)));
            if (street is not null)
                yield return query with { RegionOrCity = last, Street = street };
        }
    }

    /// <summary>
    /// Очистка строки одним проходом по <see cref="ReadOnlySpan{T}"/>: нижний регистр, ё→е,
    /// оставляем буквы/цифры/«/»/«-», всё прочее схлопываем в один пробел. Единственная
    /// аллокация — итоговая строка; буфер до 256 символов берётся со стека.
    /// </summary>
    private static string Clean(ReadOnlySpan<char> raw)
    {
        if (raw.IsEmpty) return string.Empty;

        Span<char> buffer = raw.Length <= 256 ? stackalloc char[raw.Length] : new char[raw.Length];
        var n = 0;
        var prevSpace = true; // съедаем ведущие пробелы

        foreach (var src in raw)
        {
            var ch = char.ToLowerInvariant(src);
            if (ch == 'ё') ch = 'е';

            if (char.IsLetterOrDigit(ch) || ch is '/' or '-')
            {
                buffer[n++] = ch;
                prevSpace = false;
            }
            else if (!prevSpace) // любой разделитель/мусор → одиночный пробел
            {
                buffer[n++] = ' ';
                prevSpace = true;
            }
        }

        while (n > 0 && buffer[n - 1] == ' ') n--; // хвостовой пробел
        return new string(buffer[..n]);
    }

    /// <summary>Разбивает на токены и раскрывает сокращения (одно сокращение может дать
    /// несколько токенов, напр. «нн» → «нижний», «новгород»).</summary>
    private static List<string> TokenizeAndExpand(string normalized)
    {
        var raw = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>(raw.Length + 1);

        foreach (var token in raw)
        {
            if (Abbreviations.TryGetValue(token, out var full))
            {
                if (full.Contains(' '))
                    result.AddRange(full.Split(' ', StringSplitOptions.RemoveEmptyEntries));
                else
                    result.Add(full);
            }
            else
            {
                result.Add(token);
            }
        }

        return result;
    }

    /// <summary>Структурированный номер дома: базовый номер, корпус/строение и склеенная форма.</summary>
    private readonly record struct HouseParts(string? Num, string? Building, string Merged)
    {
        public static readonly HouseParts Empty = new(null, null, string.Empty);
    }

    /// <summary>
    /// Отделяет хвостовой номер дома: с конца забираем токены, пока они «домовые»
    /// (цифра, маркер+цифра «к1», маркер «д»/«корп»). Возвращает именную часть и разбор номера.
    /// </summary>
    private static (List<string> NameTokens, HouseParts House) SplitTrailingHouse(List<string> tokens)
    {
        var splitAt = tokens.Count;
        while (splitAt > 0 && IsHouseToken(tokens[splitAt - 1]))
            splitAt--;

        // Если «дом» оказался в самом начале (вся строка — номер) — это не номер, а, вероятно,
        // деревня «д. Ивановка»: ничего не отрезаем.
        if (splitAt == 0) return (tokens, HouseParts.Empty);

        var nameTokens = tokens.GetRange(0, splitAt);
        var houseTokens = tokens.GetRange(splitAt, tokens.Count - splitAt);
        return (nameTokens, ParseHouse(houseTokens));
    }

    private static bool IsHouseToken(string token) =>
        PureHouseMarkers.Contains(token) || BuildingMarkers.Contains(token) || HouseTokenRegex().IsMatch(token);

    /// <summary>
    /// Разбирает домовые токены на базовый номер и номер корпуса/строения. Понимает три формы:
    /// раздельную («д 12 корп 1»), склеенную («12к1») и маркер+номер («к 1»). Корпус идёт в
    /// <see cref="HouseParts.Building"/>, чтобы матчиться отдельно против addnum1/addnum2.
    /// </summary>
    private static HouseParts ParseHouse(List<string> tokens)
    {
        string? num = null, building = null;
        var slotBuilding = false; // true после маркера корпуса/строения: следующий номер — корпус

        foreach (var t in tokens)
        {
            if (PureHouseMarkers.Contains(t)) { slotBuilding = false; continue; }
            if (BuildingMarkers.Contains(t)) { slotBuilding = true; continue; }

            var m = HousePartRegex().Match(t);
            if (!m.Success) continue;
            var baseNum = m.Groups["base"].Value;
            var kind = m.Groups["kind"].Value;
            var bld = m.Groups["bld"].Value;

            if (baseNum.Length > 0)
            {
                if (kind.Length > 0 || bld.Length > 0) num ??= baseNum;   // «12к1» → 12 это дом
                else if (slotBuilding) building ??= baseNum;              // «...корп 1»
                else num ??= baseNum;                                    // «12»
            }
            if (bld.Length > 0) building ??= bld;                        // «к1», «12к1»
            if (kind.Length > 0) slotBuilding = true;
        }

        if (num is null) return HouseParts.Empty;
        var merged = building is null ? num : num + "к" + building;
        return new HouseParts(num, building, merged);
    }

    /// <summary>
    /// Делит именную часть на контейнер (регион/город) и улицу. Если есть маркер улицы —
    /// делим по нему; иначе считаем первый токен контейнером, остальное — улицей. Эвристика
    /// сознательно простая: уточнение делает SQL-сужение в репозитории.
    /// </summary>
    private static (string RegionOrCity, string Street) SplitContainerAndStreet(List<string> nameTokens)
    {
        if (nameTokens.Count == 0) return (string.Empty, string.Empty);

        var streetIdx = nameTokens.FindIndex(t => StreetMarkers.Contains(t));

        if (streetIdx >= 0)
        {
            var left = StripMarkers(nameTokens.GetRange(0, streetIdx));
            var right = StripMarkers(nameTokens.GetRange(streetIdx + 1, nameTokens.Count - streetIdx - 1));

            if (right.Count > 0)
                return (string.Join(' ', left), string.Join(' ', right));

            // Постфиксный маркер: «красный проспект» → улица = последний токен слева.
            if (left.Count >= 2)
                return (string.Join(' ', left.GetRange(0, left.Count - 1)), left[^1]);
            return (string.Empty, string.Join(' ', left));
        }

        var content = StripMarkers(nameTokens);
        if (content.Count == 0) return (string.Empty, string.Empty);
        if (content.Count == 1) return (content[0], string.Empty);

        // Первый токен — одиночная буква: это инициал имени улицы («Б. Хмельницкого»,
        // «К. Маркса»), а НЕ контейнер. Вся часть уходит в улицу; раскрытие инициала в
        // полное имя делает префиксный матч FTS в репозитории (б → богдан/большая).
        if (IsInitial(content[0]))
            return (string.Empty, string.Join(' ', content));

        // «новосибирск ленина» → город + улица; «нижний новгород» → весь контейнер, улицы нет.
        return (content[0], string.Join(' ', content.GetRange(1, content.Count - 1)));
    }

    private static List<string> StripMarkers(List<string> tokens)
    {
        var result = new List<string>(tokens.Count);
        foreach (var t in tokens)
            if (!RegionMarkers.Contains(t) && !StreetMarkers.Contains(t))
                result.Add(t);
        return result;
    }

    /// <summary>Одиночная кириллическая буква — инициал имени («Б.» в «Б. Хмельницкого»).</summary>
    private static bool IsInitial(string token) => token.Length == 1 && token[0] is >= 'а' and <= 'я';

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
