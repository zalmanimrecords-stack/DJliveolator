using Liveolator.Core.Library.SmartCollections;

namespace Liveolator.Core.Persistence;

public interface ISmartCollectionStore
{
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);

    Task<SmartCollectionDefinition?> LoadAsync(string name, CancellationToken cancellationToken = default);

    Task SaveAsync(SmartCollectionDefinition definition, CancellationToken cancellationToken = default);

    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}

