using Fias.Application.Models;

namespace Fias.Application.Services;

public class BatchAddressService(IAddressBuilderService builder) : IBatchAddressService
{
    private const int MaxBatchSize = 1000;

    public async Task<BatchAddressResponse> ResolveAsync(BatchAddressRequest request, CancellationToken ct)
    {
        if (request.ObjectIds.Count == 0)
            return new BatchAddressResponse(Array.Empty<BatchAddressItem>());
        if (request.ObjectIds.Count > MaxBatchSize)
            throw new ArgumentException($"В одном запросе не больше {MaxBatchSize} идентификаторов.");

        var items = new List<BatchAddressItem>(request.ObjectIds.Count);
        foreach (var objectId in request.ObjectIds)
        {
            ct.ThrowIfCancellationRequested();
            var address = await builder.BuildByObjectIdAsync(objectId, ct);
            items.Add(new BatchAddressItem(objectId, address));
        }
        return new BatchAddressResponse(items);
    }
}
