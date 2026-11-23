namespace DawVcs.Domain.Repositories;

/// <summary>The operation's expected local branch or tip changed before publication.</summary>
public sealed class RepositoryStateChangedException(string message) : Exception(message);
