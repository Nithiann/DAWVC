using DawVcs.Application.Common;
using DawVcs.Application.Exceptions;
using DawVcs.Domain.Remotes;
using DawVcs.Domain.Storage;

using FluentAssertions;

using Xunit;

namespace DawVcs.Application.Tests.Common;

public sealed class OperationFailureTests
{
    [Fact]
    public void Classification_UsesTypesRatherThanMessageText()
    {
        OperationFailure.FromException(new InvalidOperationException("cancelled timeout integrity"))
            .Category.Should().Be(OperationFailureCategory.Unknown);
        OperationFailure.FromException(new TimeoutException("cancelled"))
            .Category.Should().Be(OperationFailureCategory.Timeout);
        OperationFailure.FromException(new OperationCanceledException("timeout"))
            .Category.Should().Be(OperationFailureCategory.Cancellation);
        OperationFailure.FromException(new UnsupportedEnvelopeVersionException("remote"))
            .Category.Should().Be(OperationFailureCategory.IncompatibleRepository);
        OperationFailure.FromException(new PayloadHashMismatchException("validation"))
            .Category.Should().Be(OperationFailureCategory.IntegrityFailure);
        OperationFailure.FromException(new RemoteException("timeout"))
            .Category.Should().Be(OperationFailureCategory.RemoteUnavailable);
    }

    [Fact]
    public void IncompleteRollback_PreservesRecoveryPathAndTechnicalCause()
    {
        var exception = new IncompleteRollbackException("local-recovery", new IOException());
        var failure = OperationFailure.FromException(exception);
        failure.Category.Should().Be(OperationFailureCategory.IncompleteRollback);
        failure.RecoveryPath.Should().Be("local-recovery");
        failure.TechnicalException.Should().BeSameAs(exception);
    }
}
