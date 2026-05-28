using Fias.Application.Models;

namespace Fias.Application.Services;

/// <summary>
/// Простой парсер свободной адресной строки.
/// Стратегия — токенизация по разделителям ([,;] и пробелам), затем подбор кандидатов
/// для каждого токена через нечёткий поиск и сборка финальной иерархии по совпадению путей.
/// Для production-grade распознавания (как DaData) нужен отдельный движок — здесь MVP.
/// </summary>
public class AddressParseService(IAddressSearchService search, IAddressBuilderService builder) : IAddressParseService
{
    private static readonly char[] Separators = [',', ';'];

    public async Task<ParseResult> ParseAsync(ParseRequest request, CancellationToken ct)
    {
        var query = (request.Address ?? string.Empty).Trim();
        if (query.Length < 3) return new ParseResult(query, Array.Empty<ParseCandidate>());

        // Грубая стратегия: ищем по самой длинной значащей подстроке (без числовых токенов),
        // получаем топ-N кандидатов с их полными адресами; score = similarity * матч_остальных_токенов.
        var tokens = Tokenize(query);
        var meaningful = tokens.Where(t => t.Length > 2 && !t.All(char.IsDigit)).ToArray();
        if (meaningful.Length == 0) return new ParseResult(query, Array.Empty<ParseCandidate>());

        // Берём самый длинный token (обычно — название улицы/города).
        var primary = meaningful.OrderByDescending(t => t.Length).First();
        var hits = await search.SearchAsync(primary, request.Limit * 3, request.Threshold, null, null, ct);

        var lowerQuery = query.ToLowerInvariant();
        var candidates = new List<ParseCandidate>(hits.Count);
        foreach (var hit in hits)
        {
            var address = await builder.BuildByObjectIdAsync(hit.ObjectId, ct);
            if (address is null) continue;

            // score: базовый similarity по primary + бонус за совпадение остальных токенов в адресной строке.
            var lowerAddr = address.Address.ToLowerInvariant();
            var matched = tokens.Count(t => t.Length > 1 && lowerAddr.Contains(t, StringComparison.Ordinal));
            var score = hit.Similarity * 0.6 + (double)matched / Math.Max(tokens.Length, 1) * 0.4;

            candidates.Add(new ParseCandidate(score, address));
        }

        return new ParseResult(query, candidates
            .OrderByDescending(c => c.Score)
            .Take(request.Limit)
            .ToList());
    }

    public async Task<ParseBatchResult> ParseBatchAsync(ParseBatchRequest request, CancellationToken ct)
    {
        if (request.Addresses.Count > 1000)
            throw new ArgumentException("В одном батче не больше 1000 адресов.");

        var results = new List<ParseResult>(request.Addresses.Count);
        foreach (var addr in request.Addresses)
        {
            ct.ThrowIfCancellationRequested();
            var r = await ParseAsync(new ParseRequest(addr, request.LimitPerItem, request.Threshold), ct);
            results.Add(r);
        }
        return new ParseBatchResult(results);
    }

    private static string[] Tokenize(string text)
    {
        return text
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(part => part.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(t => t.Trim().Trim('.'))
            .Where(t => t.Length > 0)
            .ToArray();
    }
}
