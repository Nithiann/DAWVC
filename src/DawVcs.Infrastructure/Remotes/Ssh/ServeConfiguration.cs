using System.Text.Json;
using System.Text.Json.Serialization;

using DawVcs.Domain.Remotes;

namespace DawVcs.Infrastructure.Remotes.Ssh;

public sealed record HostedRepository
{
    public string Path { get; init; } = "";
    public bool AllowPush { get; init; }
}

/// <summary>Host-owned policy, selected by the SSH account or forced command, never by the client.</summary>
public sealed record ServeConfiguration
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 8 };
    public Dictionary<string, HostedRepository> Repositories { get; init; } = new(StringComparer.Ordinal);
    public int SessionTimeoutSeconds { get; init; } = 600;
    public int MaxRequests { get; init; } = 100_000;
    public long MaxObjectBytes { get; init; } = RemoteWire.MaxObjectBytes;
    public long MaxTransferBytes { get; init; } = 32L * 1024 * 1024 * 1024;
    public static string DefaultPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "dawvc", "serve.json");

    public static ServeConfiguration Load(string path)
    {
        if (new FileInfo(path).Length > 256 * 1024) throw new RemoteException("Host policy exceeds the supported size.");
        var config = JsonSerializer.Deserialize<ServeConfiguration>(File.ReadAllBytes(path), JsonOptions)
            ?? throw new RemoteException("Host policy is empty.");
        if (config.Repositories is null || config.Repositories.Count > 1024 || config.SessionTimeoutSeconds is < 5 or > 3600 ||
            config.MaxRequests is < 1 or > 2_000_000 || config.MaxObjectBytes is < 1 or > RemoteWire.MaxObjectBytes || config.MaxTransferBytes < config.MaxObjectBytes)
            throw new RemoteException("Invalid host policy limits.");
        foreach (var entry in config.Repositories)
        {
            _ = DawVcsAddress.Parse("dawvc://policy/" + entry.Key);
            if (entry.Value is null || !System.IO.Path.IsPathFullyQualified(entry.Value.Path))
                throw new RemoteException("Hosted repository paths must be absolute.");
        }
        return config;
    }
}
