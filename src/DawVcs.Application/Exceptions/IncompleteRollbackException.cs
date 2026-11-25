namespace DawVcs.Application.Exceptions;

/// <summary>A failed rollback requiring manual recovery. Recovery data must be retained.</summary>
public sealed class IncompleteRollbackException : Exception
{
    public string RecoveryPath { get; }

    public IncompleteRollbackException(string recoveryPath, Exception innerException)
        : base("Rollback could not restore all changes. Manual recovery is required.", innerException)
    {
        RecoveryPath = recoveryPath;
    }
}
