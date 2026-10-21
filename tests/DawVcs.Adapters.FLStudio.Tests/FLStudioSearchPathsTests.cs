using System;
using System.IO;
using System.Threading.Tasks;

using FluentAssertions;

using Xunit;

namespace DawVcs.Adapters.FLStudio.Tests;

public sealed class FLStudioSearchPathsTests : IDisposable
{
    private readonly string _tempRoot;

    public FLStudioSearchPathsTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "dawvc_searchpaths_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort
        }
    }

    [Theory]
    [InlineData(@"D:\Sounds,""AUDIO SNIPPETS""", @"D:\Sounds")]
    [InlineData(@"""C:\Users\Bas\Tools\Exports\TEKKEN 8\WwiseAudio"",""TEKKEN8 Audio""", @"C:\Users\Bas\Tools\Exports\TEKKEN 8\WwiseAudio")]
    [InlineData(@"C:\Users\Bas\Documents\Sounds\,""AUDIO SNIPPETS (old)""", @"C:\Users\Bas\Documents\Sounds")]
    [InlineData(@"D:\,""DRIVE ROOT""", @"D:\")]
    [InlineData(@"E:/SamplePacks/KSHMR/,""KSHMR""", @"E:/SamplePacks/KSHMR")]
    [InlineData(@"D:\Sounds", @"D:\Sounds")]
    [InlineData(@"""D:\Sounds with spaces""", @"D:\Sounds with spaces")]
    public void ParseSearchPathEntry_ParsesValidFormatsCorrectly(string input, string expected)
    {
        var result = FLStudioAdapter.ParseSearchPathEntry(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    public void ParseSearchPathEntry_WithEmptyInput_ReturnsNull(string? input)
    {
        var result = FLStudioAdapter.ParseSearchPathEntry(input);
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveSamplePath_FindsDirectAndNestedSamples_InConfiguredSearchPaths()
    {
        var searchDir = Path.Combine(_tempRoot, "SearchFolder");
        var nestedDir = Path.Combine(searchDir, "Packs", "Hardstyle");
        Directory.CreateDirectory(nestedDir);

        var kickPath = Path.Combine(nestedDir, "OPS_Kick_01.wav");
        await File.WriteAllBytesAsync(kickPath, [0x52, 0x49, 0x46, 0x46]);

        var snarePath = Path.Combine(searchDir, "Clap.wav");
        await File.WriteAllBytesAsync(snarePath, [0x52, 0x49, 0x46, 0x46]);

        var adapter = new FLStudioAdapter([searchDir]);

        // 1. Directe bestandsnaamzoektocht via index
        var resolvedKick = adapter.ResolveSamplePath("OPS_Kick_01.wav");
        resolvedKick.Should().NotBeNull();
        resolvedKick.Should().Be(kickPath);

        // 2. Direct bestand in de root van search dir
        var resolvedSnare = adapter.ResolveSamplePath("Clap.wav");
        resolvedSnare.Should().NotBeNull();
        resolvedSnare.Should().Be(snarePath);

        // 3. Relatief subpad
        var resolvedSubpath = adapter.ResolveSamplePath("Packs/Hardstyle/OPS_Kick_01.wav");
        resolvedSubpath.Should().NotBeNull();
        resolvedSubpath.Should().Be(kickPath);

        // 4. Niet-bestaand bestand
        var missing = adapter.ResolveSamplePath("UnknownFile.wav");
        missing.Should().BeNull();
    }
}
