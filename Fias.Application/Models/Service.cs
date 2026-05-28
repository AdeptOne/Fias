namespace Fias.Application.Models;

public record VersionDto(int? VersionId, string? TextVersion, DateTime? AppliedAt);

public record StatsDto(
    long AddressObjects,
    long Houses,
    long Apartments,
    long Rooms,
    long Regions);

public record AdminImportRequest(string? LocalZipPath);

public record AdminImportResponse(string JobId);
