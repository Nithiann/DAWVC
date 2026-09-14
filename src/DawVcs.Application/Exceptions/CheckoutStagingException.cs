namespace DawVcs.Application.Exceptions;

/// <summary>
/// Wordt gegooid wanneer een integriteitsverificatie tijdens staged checkout faalt (FR-CHK-005, FR-CHK-007, AC-011).
/// Garandeert dat een mislukte checkout nooit wordt gepubliceerd.
/// </summary>
public sealed class CheckoutStagingException : InvalidOperationException
{
    public CheckoutStagingException(string message)
        : base(message)
    {
    }

    public CheckoutStagingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
