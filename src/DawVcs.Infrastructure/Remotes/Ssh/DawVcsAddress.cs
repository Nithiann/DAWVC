using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DawVcs.Infrastructure.Remotes.Ssh;

/// <summary>A host and logical repository alias, never a server filesystem path.</summary>
public sealed partial record DawVcsAddress(string Host, string? User, int Port, string Repository)
{
    public static DawVcsAddress Parse(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        if (!address.StartsWith("dawvc://", StringComparison.OrdinalIgnoreCase) || address.Any(char.IsWhiteSpace) ||
            address.Contains('?') || address.Contains('#') || address.Contains('\\') ||
            !Uri.TryCreate(address, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            throw new ArgumentException("Use dawvc://[user@]host[:port]/repository-alias.", nameof(address));
        if (uri.HostNameType is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6))
            throw new ArgumentException("Invalid SSH hostname.", nameof(address));
        var user = uri.UserInfo.Length == 0 ? null : Uri.UnescapeDataString(uri.UserInfo);
        if (user is not null && !UserPattern().IsMatch(user))
            throw new ArgumentException("SSH usernames must not contain passwords, options or shell characters.", nameof(address));
        var slash = address.IndexOf('/', "dawvc://".Length);
        if (slash < 0) throw new ArgumentException("A repository alias is required.", nameof(address));
        // Validate the original path before System.Uri can normalize dot segments.
        var raw = address[(slash + 1)..];
        var parts = raw.Split('/');
        foreach (var part in parts)
        {
            if (part.Contains('%')) throw new ArgumentException("Repository aliases must use literal ASCII names, without URI escapes.", nameof(address));
            if (!AliasPartPattern().IsMatch(part) || part is "." or "..")
                throw new ArgumentException("Invalid repository alias component.", nameof(address));
        }
        if (raw.Length > 256 || uri.Host.Length > 253 || uri.Port is 0 or > 65535)
            throw new ArgumentException("Remote address exceeds supported limits.", nameof(address));
        return new DawVcsAddress(uri.IdnHost, user, uri.Port < 0 ? 22 : uri.Port, raw);
    }

    public ProcessStartInfo CreateStartInfo()
    {
        var start = new ProcessStartInfo("ssh") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("-T");
        foreach (var option in new[] { "BatchMode=yes", "StrictHostKeyChecking=yes", "VerifyHostKeyDNS=no", "UpdateHostKeys=no", "PasswordAuthentication=no", "KbdInteractiveAuthentication=no", "PreferredAuthentications=publickey", "ForwardAgent=no", "ForwardX11=no", "ClearAllForwardings=yes", "PermitLocalCommand=no", "ConnectTimeout=15", "ServerAliveInterval=15", "ServerAliveCountMax=2" })
        {
            start.ArgumentList.Add("-o");
            start.ArgumentList.Add(option);
        }
        start.ArgumentList.Add("-p");
        start.ArgumentList.Add(Port.ToString(CultureInfo.InvariantCulture));
        if (User is not null) { start.ArgumentList.Add("-l"); start.ArgumentList.Add(User); }
        start.ArgumentList.Add("--");
        start.ArgumentList.Add(Host);
        start.ArgumentList.Add("dawvc serve");
        return start;
    }

    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex UserPattern();
    [GeneratedRegex("^[A-Za-z0-9_-][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex AliasPartPattern();
}
