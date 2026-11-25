namespace DawVcs.Domain.Repositories;

/// <summary>An operation's selected local repository state changed before materialization.</summary>
public sealed class RepositoryStateChangedException(string message) : InvalidOperationException(message);
