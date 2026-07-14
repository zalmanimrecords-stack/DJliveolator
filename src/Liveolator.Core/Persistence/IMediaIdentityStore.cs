using Liveolator.Core.Library.Doctor;

namespace Liveolator.Core.Persistence;

public interface IMediaIdentityStore
{
    Task<IReadOnlyList<MediaIdentity>> LoadIdentitiesAsync(CancellationToken cancellationToken = default);

    Task SaveIdentitiesAsync(IEnumerable<MediaIdentity> identities, CancellationToken cancellationToken = default);
}

