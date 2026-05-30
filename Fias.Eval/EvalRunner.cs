using System.Text;
using Fias.Application.Services;

namespace Fias.Eval;

/// <summary>Накопитель метрик одного бакета: recall@1/5/10 и сумма обратных рангов для MRR.</summary>
public sealed class BucketAccumulator(string bucket)
{
    public string Bucket { get; } = bucket;
    public int Total { get; private set; }
    public int Hit1 { get; private set; }
    public int Hit5 { get; private set; }
    public int Hit10 { get; private set; }
    private double _mrrSum;

    /// <summary>rank — 1-based позиция эталона в выдаче; 0, если эталон не вернулся вовсе.</summary>
    public void Add(int rank)
    {
        Total++;
        if (rank == 0) return;
        if (rank <= 1) Hit1++;
        if (rank <= 5) Hit5++;
        if (rank <= 10) Hit10++;
        _mrrSum += 1.0 / rank;
    }

    public double Recall1 => Total == 0 ? 0 : (double)Hit1 / Total;
    public double Recall5 => Total == 0 ? 0 : (double)Hit5 / Total;
    public double Recall10 => Total == 0 ? 0 : (double)Hit10 / Total;
    public double Mrr => Total == 0 ? 0 : _mrrSum / Total;
}

/// <summary>Промах для CSV-отчёта: что спросили, что ждали, что вернулось первым.</summary>
public sealed record Miss(string Bucket, string Query, Guid Expected, string ExpectedLabel, int Rank, string Top1);

/// <summary>
/// Прогоняет золотой набор через боевой <see cref="IAddressSearchService"/> (тот же путь, что
/// у API: сырая строка → нормализация → гибридный поиск) и считает recall@k / MRR по бакетам.
/// </summary>
public sealed class EvalRunner(IAddressSearchService search)
{
    public sealed record Report(IReadOnlyList<BucketAccumulator> Buckets, BucketAccumulator Overall, IReadOnlyList<Miss> Misses);

    public async Task<Report> RunAsync(IReadOnlyList<GoldenItem> golden, int limit, CancellationToken ct)
    {
        var buckets = new Dictionary<string, BucketAccumulator>();
        var overall = new BucketAccumulator("ALL");
        var misses = new List<Miss>();

        foreach (var item in golden)
        {
            ct.ThrowIfCancellationRequested();
            var hits = await search.SearchAsync(item.Query, limit, ct);

            var rank = 0;
            for (var i = 0; i < hits.Count; i++)
                if (hits[i].ObjectGuid == item.ExpectedGuid) { rank = i + 1; break; }

            var acc = buckets.TryGetValue(item.Bucket, out var a) ? a : buckets[item.Bucket] = new BucketAccumulator(item.Bucket);
            acc.Add(rank);
            overall.Add(rank);

            if (rank == 0 || rank > 1)
            {
                var top1 = hits.Count > 0 ? $"{hits[0].FullName} [{hits[0].ObjectGuid}]" : "—";
                misses.Add(new Miss(item.Bucket, item.Query, item.ExpectedGuid, item.ExpectedLabel, rank, top1));
            }
        }

        return new Report(buckets.Values.OrderBy(b => b.Bucket).ToList(), overall, misses);
    }

    /// <summary>Таблица метрик по бакетам в консоль.</summary>
    public static string FormatTable(Report report)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"{"bucket",-10} {"n",6} {"R@1",8} {"R@5",8} {"R@10",8} {"MRR",8}");
        sb.AppendLine(new string('-', 52));
        foreach (var b in report.Buckets)
            sb.AppendLine(Row(b));
        sb.AppendLine(new string('-', 52));
        sb.AppendLine(Row(report.Overall));
        return sb.ToString();

        static string Row(BucketAccumulator b) =>
            $"{b.Bucket,-10} {b.Total,6} {b.Recall1,8:P1} {b.Recall5,8:P1} {b.Recall10,8:P1} {b.Mrr,8:F3}";
    }

    /// <summary>CSV промахов (рекалл-мисс или эталон не на 1-й позиции) для ручного разбора.</summary>
    public static string FormatMissesCsv(IReadOnlyList<Miss> misses)
    {
        var sb = new StringBuilder();
        sb.AppendLine("bucket,query,rank,expected_guid,expected_label,top1");
        foreach (var m in misses)
            sb.AppendLine($"{Csv(m.Bucket)},{Csv(m.Query)},{m.Rank},{m.Expected},{Csv(m.ExpectedLabel)},{Csv(m.Top1)}");
        return sb.ToString();

        static string Csv(string s) => $"\"{s.Replace("\"", "\"\"")}\"";
    }
}
