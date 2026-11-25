namespace DawVcs.Domain.Repositories;

/// <summary>Unfinished workspace publication or conflicting recovery state must be resolved first.</summary>
public sealed class WorkspaceRecoveryRequiredException(string message) : Exception(message);
