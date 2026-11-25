namespace DawVcs.Domain.Repositories;

/// <summary>A cooperating repository operation already owns the local writer lock.</summary>
public sealed class RepositoryBusyException(string message, Exception? innerException = null) : Exception(message, innerException);
