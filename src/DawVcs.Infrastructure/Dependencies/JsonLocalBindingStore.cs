using System.Text.Json;
using System.Text.Json.Nodes;

using DawVcs.Domain.Dependencies;
using DawVcs.Domain.Hashing;
using DawVcs.Domain.Serialization;
using DawVcs.Infrastructure.FileSystem;

namespace DawVcs.Infrastructure.Dependencies;

/// <summary>
/// Filesystem-based persistence for local dependency bindings in .dawvc/bindings.json (FR-BND-001, FR-BND-009).
/// </summary>
public sealed class JsonLocalBindingStore : ILocalBindingStore, IDisposable
{
    private const int CurrentSchemaVersion = 1;
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonLocalBindingStore(string dotDawvcPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dotDawvcPath);
        _filePath = Path.Combine(dotDawvcPath, "bindings.json");
    }

    public void Dispose()
    {
        _lock.Dispose();
    }

    public async Task<IReadOnlyList<DependencyBinding>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<DependencyBinding?> GetAsync(DependencyId id, CancellationToken cancellationToken = default)
    {
        var all = await GetAllAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(b => b.DependencyId == id);
    }

    public async Task SaveAsync(DependencyBinding binding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var bindings = (await LoadInternalAsync(cancellationToken).ConfigureAwait(false)).ToList();
            var existingIndex = bindings.FindIndex(b => b.DependencyId == binding.DependencyId);
            if (existingIndex >= 0)
            {
                bindings[existingIndex] = binding;
            }
            else
            {
                bindings.Add(binding);
            }

            await SaveInternalAsync(bindings, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAllAsync(IEnumerable<DependencyBinding> bindings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bindings);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dict = (await LoadInternalAsync(cancellationToken).ConfigureAwait(false))
                .ToDictionary(b => b.DependencyId, b => b);

            foreach (var b in bindings)
            {
                dict[b.DependencyId] = b;
            }

            await SaveInternalAsync(dict.Values.ToList(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveAsync(DependencyId id, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var bindings = (await LoadInternalAsync(cancellationToken).ConfigureAwait(false)).ToList();
            var removed = bindings.RemoveAll(b => b.DependencyId == id);
            if (removed > 0)
            {
                await SaveInternalAsync(bindings, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<DependencyBinding>> LoadInternalAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        byte[] bytes;
        await using (var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous))
        {
            using var ms = new MemoryStream();
            await fs.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            bytes = ms.ToArray();
        }

        if (bytes.Length == 0)
        {
            return [];
        }

        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        if (!root.TryGetProperty("bindings", out var bindingsArray) || bindingsArray.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var result = new List<DependencyBinding>();
        foreach (var element in bindingsArray.EnumerateArray())
        {
            var idStr = element.GetProperty("dependencyId").GetString()!;
            var locator = element.GetProperty("locator").GetString()!;
            var methodStr = element.GetProperty("method").GetString()!;
            var statusStr = element.GetProperty("status").GetString()!;

            ContentHash? verifiedHash = null;
            if (element.TryGetProperty("verifiedHash", out var hashEl) && hashEl.ValueKind == JsonValueKind.String)
            {
                var hashVal = hashEl.GetString();
                if (!string.IsNullOrEmpty(hashVal))
                {
                    verifiedHash = ContentHash.Parse(hashVal);
                }
            }

            var boundAt = element.GetProperty("boundAt").GetDateTimeOffset();
            string? notes = null;
            if (element.TryGetProperty("notes", out var notesEl) && notesEl.ValueKind == JsonValueKind.String)
            {
                notes = notesEl.GetString();
            }

            var method = Enum.Parse<BindingMethod>(methodStr, ignoreCase: true);
            var status = Enum.Parse<BindingStatus>(statusStr, ignoreCase: true);

            result.Add(new DependencyBinding(new DependencyId(idStr), locator, method, status, verifiedHash, boundAt, notes));
        }

        return result;
    }

    private async Task SaveInternalAsync(IReadOnlyList<DependencyBinding> bindings, CancellationToken cancellationToken)
    {
        var bindingDtos = bindings
            .OrderBy(b => b.DependencyId.Value, StringComparer.Ordinal)
            .Select(b => new
            {
                dependencyId = b.DependencyId.Value,
                locator = b.Locator,
                method = b.Method.ToString(),
                status = b.Status.ToString(),
                verifiedHash = b.VerifiedHash?.ToString(),
                boundAt = b.BoundAt,
                notes = b.Notes
            })
            .ToList();

        var document = new
        {
            schemaVersion = CurrentSchemaVersion,
            bindings = bindingDtos
        };

        var canonicalBytes = CanonicalJsonSerializer.SerializeCanonical(document);

        await AtomicFileWriter.WriteAtomicAsync(_filePath, async stream =>
        {
            await stream.WriteAsync(canonicalBytes, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }
}
