using System.Text;
using System.Text.RegularExpressions;

using FluentAssertions;

using Xunit;

namespace DawVcs.Adapters.FLStudio.Tests.Diagnostics;

public sealed class FixturePrivacyTests
{
    private static readonly Regex WindowsUserRegex = new(@"[a-zA-Z]:\\Users\\[^\x00\r\n\t\\/]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex UnixUserRegex = new(@"/(Users|home)/[^\x00\r\n\t/]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EmailRegex = new(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b", RegexOptions.Compiled);

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "DawVcs.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not find solution root.");
    }

    [Fact]
    [Trait("Category", "Privacy")]
    public void Fixtures_MustNotContainPersonalPathsOrEmails()
    {
        var repoRoot = FindRepoRoot();
        var fixturesDir = Path.Combine(repoRoot, "fixtures");

        if (!Directory.Exists(fixturesDir))
        {
            return;
        }

        var files = Directory.EnumerateFiles(fixturesDir, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && !f.EndsWith(".gitkeep", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var violations = new List<string>();

        foreach (var file in files)
        {
            var relPath = Path.GetRelativePath(repoRoot, file);
            var bytes = File.ReadAllBytes(file);

            var ascii = Encoding.ASCII.GetString(bytes);
            var utf8 = Encoding.UTF8.GetString(bytes);
            var utf16 = Encoding.Unicode.GetString(bytes);

            foreach (var content in new[] { ascii, utf8, utf16 })
            {
                foreach (Match match in WindowsUserRegex.Matches(content))
                {
                    violations.Add($"{relPath}: Contains Windows User Profile path: '{match.Value}'");
                }

                foreach (Match match in UnixUserRegex.Matches(content))
                {
                    violations.Add($"{relPath}: Contains Unix User Profile path: '{match.Value}'");
                }

                foreach (Match match in EmailRegex.Matches(content))
                {
                    violations.Add($"{relPath}: Contains Email address: '{match.Value}'");
                }
            }
        }

        var uniqueViolations = violations.Distinct().ToList();
        uniqueViolations.Should().BeEmpty(
            because: "fixtures must adhere to the zero-user-data privacy policy and not leak local usernames, collaborator profiles, or emails.\nViolations found:\n" +
                     string.Join("\n", uniqueViolations));
    }
}
