namespace DawVcs.Infrastructure.Common;

/// <summary>
/// Basiscontract voor interactie met de content-addressed object store.
/// </summary>
public interface IObjectStore
{
    bool Exists(string contentHash);
}
