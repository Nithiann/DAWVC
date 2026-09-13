namespace DawVcs.Adapters.Abstractions;

/// <summary>
/// Read-only context voor DAW-adapters.
/// Garandeert dat adapters uitsluitend leesrechten hebben en nooit een writehandle naar het bronbestand ontvangen (FR-FLP-002, IMP-0502).
/// </summary>
public sealed class ArtifactReadContext : IDisposable, IAsyncDisposable
{
    private readonly Func<Stream>? _streamFactory;
    private readonly Stream? _underlyingStream;
    private readonly bool _leaveOpen;
    private Stream? _activeStream;

    public string? FilePath { get; }
    public string FileName => string.IsNullOrEmpty(FilePath) ? "stream" : Path.GetFileName(FilePath);
    public string Extension => string.IsNullOrEmpty(FilePath) ? string.Empty : Path.GetExtension(FilePath);
    public long? FileLength { get; }

    public ArtifactReadContext(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Artifact not found at '{filePath}'.", filePath);
        }

        FilePath = filePath;
        var info = new FileInfo(filePath);
        FileLength = info.Length;
        _streamFactory = () => new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public ArtifactReadContext(Stream stream, string? fileName = null, bool leaveOpen = true)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _underlyingStream = stream;
        _leaveOpen = leaveOpen;
        FilePath = fileName;
        if (stream.CanSeek)
        {
            FileLength = stream.Length;
        }
    }

    /// <summary>
    /// Opent of levert een strikt alleen-lezen stream naar het bronartifact.
    /// </summary>
    public Stream OpenReadStream()
    {
        if (_streamFactory != null)
        {
            _activeStream?.Dispose();
            _activeStream = _streamFactory();
            return _activeStream;
        }

        if (_underlyingStream != null)
        {
            if (_underlyingStream.CanSeek)
            {
                _underlyingStream.Seek(0, SeekOrigin.Begin);
            }
            return _underlyingStream;
        }

        throw new InvalidOperationException("No stream or stream factory available in ArtifactReadContext.");
    }

    public void Dispose()
    {
        _activeStream?.Dispose();
        _activeStream = null;

        if (!_leaveOpen)
        {
            _underlyingStream?.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_activeStream is IAsyncDisposable asyncDisposableActive)
        {
            await asyncDisposableActive.DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            _activeStream?.Dispose();
        }
        _activeStream = null;

        if (!_leaveOpen && _underlyingStream != null)
        {
            if (_underlyingStream is IAsyncDisposable asyncDisposableUnderlying)
            {
                await asyncDisposableUnderlying.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                _underlyingStream.Dispose();
            }
        }
    }
}
