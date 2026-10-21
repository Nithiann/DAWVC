namespace DawVcs.Domain.Remotes;

/// <summary>
/// Exception thrown when remote communication, handshake, or transport fails (Exit Code 8: RemoteError).
/// </summary>
public class RemoteException : Exception
{
    public RemoteException(string message) : base(message)
    {
    }

    public RemoteException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
