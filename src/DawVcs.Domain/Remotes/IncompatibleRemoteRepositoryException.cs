namespace DawVcs.Domain.Remotes;

/// <summary>A remote repository has an unsupported format or a conflicting identity.</summary>
public sealed class IncompatibleRemoteRepositoryException : RemoteException
{
    public IncompatibleRemoteRepositoryException(string message) : base(message)
    {
    }
}
