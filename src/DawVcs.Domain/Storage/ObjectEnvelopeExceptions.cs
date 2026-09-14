namespace DawVcs.Domain.Storage;

public class InvalidEnvelopeException : Exception
{
    public InvalidEnvelopeException(string message) : base(message) { }
    public InvalidEnvelopeException(string message, Exception innerException) : base(message, innerException) { }
}

public class InvalidMagicBytesException : InvalidEnvelopeException
{
    public InvalidMagicBytesException(string message) : base(message) { }
}

public class UnsupportedEnvelopeVersionException : InvalidEnvelopeException
{
    public UnsupportedEnvelopeVersionException(string message) : base(message) { }
}

public class UnsupportedCompressionModeException : InvalidEnvelopeException
{
    public UnsupportedCompressionModeException(string message) : base(message) { }
}

public class PayloadLengthMismatchException : InvalidEnvelopeException
{
    public PayloadLengthMismatchException(string message) : base(message) { }
}

public class PayloadHashMismatchException : InvalidEnvelopeException
{
    public PayloadHashMismatchException(string message) : base(message) { }
}
