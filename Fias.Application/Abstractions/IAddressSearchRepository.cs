namespace Fias.Application.Abstractions;

/// <summary>
/// Репозиторий нечёткого поиска. Прячет за собой провайдер-специфичные функции
/// (например, pg_trgm similarity), чтобы Application не зависел от EF.Functions.
/// </summary>
public interface IAddressSearchRepository
{
    Task<IReadOnlyList<AddressSearchHit>> SearchByNameAsync(
        string query, int limit, double threshold, CancellationToken ct);
}

/// <summary>Сырая строка результата поиска до построения адресной строки.</summary>
public record AddressSearchHit(
    long ObjectId,
    Guid? ObjectGuid,
    int? Level,
    string? Name,
    string? TypeName,
    double Similarity);
