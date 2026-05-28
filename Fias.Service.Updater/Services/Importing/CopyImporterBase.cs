using System.Xml;
using Fias.Service.Updater.Options;
using Fias.Service.Updater.Services.Archives;
using Fias.Service.Updater.Services.Db;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Fias.Service.Updater.Services.Importing;

/// <summary>
/// Базовый класс для COPY-импортёров.
/// Подкласс описывает: имя таблицы, имя XML-элемента-записи, список колонок,
/// сериализацию очередного элемента в COPY-строку и SQL UPSERT для дельты.
/// </summary>
public abstract class CopyImporterBase(
    INpgsqlConnectionFactory factory,
    IOptions<FiasOptions> options,
    ILogger logger) : IFiasEntityImporter
{
    private readonly FiasOptions _options = options.Value;

    public abstract FiasEntityKind Kind { get; }

    /// <summary>Полное имя таблицы (например, "fias.addressobjects").</summary>
    protected abstract string TableName { get; }

    /// <summary>Имя XML-элемента, представляющего одну запись (например, "OBJECT").</summary>
    protected abstract string RecordElementName { get; }

    /// <summary>Список колонок таблицы в порядке записи в COPY.</summary>
    protected abstract IReadOnlyList<string> Columns { get; }

    /// <summary>Записать одно значение записи в COPY-стрим. Должно записать ровно Columns.Count значений.</summary>
    protected abstract Task WriteRowAsync(NpgsqlBinaryImporter writer, XmlReader reader, CancellationToken ct);

    /// <summary>SQL UPSERT'а из staging в основную таблицу (для дельт).</summary>
    protected abstract string BuildUpsertFromStagingSql(string stagingTable);

    public async Task<long> ImportAsync(Stream xml, ImportMode mode, CancellationToken ct)
    {
        await using var conn = await factory.OpenAsync(ct);

        var targetTable = mode == ImportMode.Full
            ? TableName
            : $"_stg_{Path.GetRandomFileName().Replace(".", "")}";

        if (mode == ImportMode.Delta)
            await CreateStagingTableAsync(conn, targetTable, ct);

        var count = await CopyAsync(conn, xml, targetTable, ct);

        if (mode == ImportMode.Delta)
        {
            await UpsertFromStagingAsync(conn, targetTable, ct);
            await DropStagingTableAsync(conn, targetTable, ct);
        }

        logger.LogInformation("{Table} ({Mode}) — {Count} записей", TableName, mode, count);
        return count;
    }

    private async Task<long> CopyAsync(NpgsqlConnection conn, Stream xml, string targetTable, CancellationToken ct)
    {
        var columns = string.Join(", ", Columns);
        var copyCmd = $"COPY {targetTable} ({columns}) FROM STDIN (FORMAT BINARY)";

        var settings = new XmlReaderSettings
        {
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            Async = false
        };

        using var reader = XmlReader.Create(xml, settings);
        await using var writer = await conn.BeginBinaryImportAsync(copyCmd, ct);

        var count = 0L;
        var batchSize = _options.CopyBatchSize;

        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element ||
                !string.Equals(reader.Name, RecordElementName, StringComparison.OrdinalIgnoreCase))
                continue;

            await writer.StartRowAsync(ct);
            await WriteRowAsync(writer, reader, ct);
            count++;

            if (batchSize > 0 && count % batchSize == 0)
                logger.LogDebug("{Table}: {Count}", TableName, count);
        }

        await writer.CompleteAsync(ct);
        return count;
    }

    private async Task CreateStagingTableAsync(NpgsqlConnection conn, string table, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            $"CREATE TEMP TABLE {table} (LIKE {TableName} INCLUDING DEFAULTS) ON COMMIT DROP", conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private async Task UpsertFromStagingAsync(NpgsqlConnection conn, string staging, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(BuildUpsertFromStagingSql(staging), conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task DropStagingTableAsync(NpgsqlConnection conn, string staging, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand($"DROP TABLE IF EXISTS {staging}", conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    protected async Task TruncateAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand($"TRUNCATE TABLE {TableName}", conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
