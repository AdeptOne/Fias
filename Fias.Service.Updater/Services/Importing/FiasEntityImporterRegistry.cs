using Fias.Service.Updater.Services.Archives;

namespace Fias.Service.Updater.Services.Importing;

public interface IFiasEntityImporterRegistry
{
    IFiasEntityImporter? Resolve(FiasEntityKind kind);
}

public class FiasEntityImporterRegistry(IEnumerable<IFiasEntityImporter> importers) : IFiasEntityImporterRegistry
{
    private readonly Dictionary<FiasEntityKind, IFiasEntityImporter> _byKind =
        importers.ToDictionary(i => i.Kind);

    public IFiasEntityImporter? Resolve(FiasEntityKind kind)
        => _byKind.GetValueOrDefault(kind);
}
