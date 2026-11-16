namespace DawVcs.Domain.Repositories;

/// <summary>The repository format cannot be consumed by this client.</summary>
public sealed class IncompatibleRepositoryException : InvalidOperationException
{
    public IncompatibleRepositoryException(string message) : base(message)
    {
    }
}
