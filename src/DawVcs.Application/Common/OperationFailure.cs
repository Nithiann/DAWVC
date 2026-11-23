using DawVcs.Application.Exceptions;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;

namespace DawVcs.Application.Common;

public enum OperationFailureCategory
{
    Unknown,
    Validation,
    DirtyWorkspace,
    MissingDependency,
    IntegrityFailure,
    IncompatibleRepository,
    RemoteUnavailable,
    NonFastForward,
    PermissionOrIoFailure,
    Timeout,
    Cancellation,
    IncompleteRollback
}

/// <summary>
/// Presentation-neutral classification. Technical details and recovery paths are local,
/// potentially sensitive data; callers must redact them before logging or exporting.
/// </summary>
public sealed record OperationFailure(OperationFailureCategory Category, Exception TechnicalException, string? RecoveryPath = null)
{
    public static OperationFailure FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var category = exception switch
        {
            IncompleteRollbackException => OperationFailureCategory.IncompleteRollback,
            DirtyWorkspaceException => OperationFailureCategory.DirtyWorkspace,
            IncompleteDependencyException => OperationFailureCategory.MissingDependency,
            RejectedNonFastForwardException => OperationFailureCategory.NonFastForward,
            IncompatibleRepositoryException or IncompatibleRemoteRepositoryException or UnsupportedEnvelopeVersionException or UnsupportedCompressionModeException => OperationFailureCategory.IncompatibleRepository,
            CheckoutStagingException { InnerException: IOException or UnauthorizedAccessException } => OperationFailureCategory.PermissionOrIoFailure,
            StagingIndexCorruptException or InvalidEnvelopeException or CheckoutStagingException => OperationFailureCategory.IntegrityFailure,
            TimeoutException => OperationFailureCategory.Timeout,
            OperationCanceledException => OperationFailureCategory.Cancellation,
            RemoteException => OperationFailureCategory.RemoteUnavailable,
            UnauthorizedAccessException or IOException => OperationFailureCategory.PermissionOrIoFailure,
            ArgumentException or PathSecurityException => OperationFailureCategory.Validation,
            _ => OperationFailureCategory.Unknown
        };
        return new OperationFailure(category, exception, (exception as IncompleteRollbackException)?.RecoveryPath);
    }
}
