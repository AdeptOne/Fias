namespace Fias.Application.Models;

public record ParseRequest(string Address, int Limit = 5, double Threshold = 0.3);

public record ParseCandidate(double Score, AddressDto Address);

public record ParseResult(string Query, IReadOnlyList<ParseCandidate> Candidates);

public record ParseBatchRequest(IReadOnlyList<string> Addresses, int LimitPerItem = 3, double Threshold = 0.3);

public record ParseBatchResult(IReadOnlyList<ParseResult> Results);
