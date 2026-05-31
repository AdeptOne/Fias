namespace Fias.Application.Search;

/// <summary>Коды качества разбора адреса в стиле DaData: <see cref="Qc"/> — нужна ли ручная
/// проверка, <see cref="QcComplete"/> — пригодность к почтовой рассылке.</summary>
public readonly record struct AddressQuality(int Qc, int QcComplete);

/// <summary>
/// Присваивает распознанному адресу коды качества (семантика DaData) по сигналам пайплайна:
/// что просили (разбор <see cref="ParsedAddressQuery"/>), что нашли (топ-выдача), сколько
/// равнозначных вариантов и был ли задействован recall-фолбэк.
///
/// <para><b>qc</b> — нужно ли проверять вручную: 0 — уверенно; 1 — лишние части / тонкий
/// контекст / неуверенный разбор; 2 — пусто/мусор/иностранный; 3 — есть альтернативы.</para>
///
/// <para><b>qc_complete</b> — годится ли для доставки: 0 — да (дом+квартира); 5 — дом без
/// квартиры (ЮЛ/частные); 10 — дом не найден в ФИАС (есть улица, дома нет); 8 — а/я или до
/// востребования; 9 — сначала проверьте разбор; 1 — нет региона; 2 — нет города; 3 — нет улицы;
/// 4 — нет дома; 6 — неполный; 7 — иностранный.</para>
/// </summary>
public static class AddressQualityClassifier
{
    private const int RegionLevel = 1;
    private const int StreetLevel = 8;
    private const int HouseLevel = 10;

    // qc-коды.
    private const int QcConfident = 0, QcReview = 1, QcEmpty = 2, QcAlternatives = 3;

    // qc_complete-коды.
    private const int QccDeliverable = 0, QccNoRegion = 1, QccNoCity = 2, QccNoStreet = 3,
        QccNoHouse = 4, QccNoFlat = 5, QccIncomplete = 6, QccForeign = 7, QccPoBox = 8,
        QccCheckParse = 9, QccHouseNotInFias = 10;

    private static readonly string[] PoBoxMarkers =
        { "а/я", "а\\я", "абонентский ящик", "почтовый ящик", "до востребования" };

    /// <summary>Главный метод: сырая строка + разбор + ранжированная выдача → коды качества.</summary>
    public static AddressQuality Classify(
        string raw, ParsedAddressQuery parsed, IReadOnlyList<AddressResult> hits, bool usedFallback)
    {
        var trimmed = (raw ?? string.Empty).Trim();
        var lower = trimmed.ToLowerInvariant();

        // Иностранный адрес: ни одной кириллической буквы, но буквы есть. Распознать нечем.
        if (HasLetters(trimmed) && !HasCyrillic(trimmed))
            return new AddressQuality(QcEmpty, QccForeign);

        // Пустой / заведомо мусорный вход либо ничего не нашли.
        if (trimmed.Length < 2 || hits.Count == 0)
            return new AddressQuality(QcEmpty, QccIncomplete);

        var best = hits[0];
        var poBox = PoBoxMarkers.Any(m => lower.Contains(m));

        // --- qc -------------------------------------------------------------
        int qc;
        if (HasAlternatives(hits))
            qc = QcAlternatives;
        else if (usedFallback || HasLeftover(parsed, best))
            qc = QcReview;
        else
            qc = QcConfident;

        // --- qc_complete ----------------------------------------------------
        var qcComplete = ClassifyCompleteness(parsed, best, qc, poBox);

        return new AddressQuality(qc, qcComplete);
    }

    /// <summary>Полнота резолва → пригодность к рассылке. Лестница от самого полного (дом) к
    /// самому бедному (только регион); поверх — спецслучаи а/я и «есть альтернативы».</summary>
    private static int ClassifyCompleteness(ParsedAddressQuery parsed, AddressResult best, int qc, bool poBox)
    {
        if (poBox) return QccPoBox;            // а/я / до востребования — для писем, не для курьера
        if (qc == QcAlternatives) return QccCheckParse; // сначала проверьте разбор

        var level = best.Level ?? 0;
        var hasHouse = level >= HouseLevel;
        var hasStreet = level == StreetLevel || best.Street is { Length: > 0 };
        var hasCity = best.City is { Length: > 0 } || best.Settlement is { Length: > 0 };
        var hasRegion = level >= RegionLevel && best.Region is { Length: > 0 };

        if (hasHouse) return QccNoFlat;        // дом есть, квартиру не резолвим → годен для ЮЛ/частных
        // Улица есть, дома нет: если дом ПРОСИЛИ, но не нашли — его нет в ФИАС (10); иначе просто нет дома (4).
        if (hasStreet) return parsed.House is not null ? QccHouseNotInFias : QccNoHouse;
        if (hasCity) return QccNoStreet;
        if (hasRegion) return QccNoCity;
        return QccNoRegion;
    }

    /// <summary>Альтернативы: топ-2 (и далее) — это РАЗНЫЕ объекты с тем же именем и уровнем при
    /// почти равном скоре («Москва Тверская-Ямская» — четыре улицы). Признак неоднозначности.</summary>
    private static bool HasAlternatives(IReadOnlyList<AddressResult> hits)
    {
        if (hits.Count < 2) return false;
        var a = hits[0];
        var b = hits[1];
        if (a.ObjectId == b.ObjectId) return false;
        if (a.Level != b.Level) return false;
        if (!NameEquals(a.Name, b.Name)) return false;
        // Скоры в одной шкале (оба из одного запроса) → относительная близость.
        var top = Math.Abs(a.Score);
        return top <= 0 || Math.Abs(a.Score - b.Score) <= top * 0.05;
    }

    /// <summary>«Лишние части»: значимый токен запроса (≥3 букв, не маркер, не номер) не встретился
    /// в денормализованной строке результата — напр. лишняя «Тверская область» при «…Москва…».
    /// Опечатки тоже сюда попадают (токен ≠ исправленному имени) — их и стоит проверить вручную.</summary>
    private static bool HasLeftover(ParsedAddressQuery parsed, AddressResult best)
    {
        var haystack = (best.FullName ?? string.Empty).ToLowerInvariant();
        if (haystack.Length == 0) return false;

        foreach (var token in parsed.Tokens)
        {
            if (token.Length < 3) continue;                 // короткие токены/инициалы пропускаем
            if (token.Any(char.IsDigit)) continue;          // номера домов не ищем в имени
            if (!HasCyrillic(token)) continue;
            if (!haystack.Contains(token)) return true;
        }
        return false;
    }

    private static bool NameEquals(string? x, string? y) =>
        string.Equals(x?.Trim(), y?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool HasCyrillic(string s)
    {
        foreach (var c in s)
            if (c is (>= 'а' and <= 'я') or (>= 'А' and <= 'Я') or 'ё' or 'Ё') return true;
        return false;
    }

    private static bool HasLetters(string s)
    {
        foreach (var c in s)
            if (char.IsLetter(c)) return true;
        return false;
    }
}
