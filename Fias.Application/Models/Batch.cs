namespace Fias.Application.Models;

public record BatchAddressRequest(IReadOnlyList<long> ObjectIds);

public record BatchAddressResponse(IReadOnlyList<BatchAddressItem> Items);

public record BatchAddressItem(long ObjectId, AddressDto? Address);
