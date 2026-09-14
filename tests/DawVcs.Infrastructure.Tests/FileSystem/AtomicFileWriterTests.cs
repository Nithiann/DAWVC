using System.Text;

using DawVcs.Infrastructure.FileSystem;

using FluentAssertions;

using Xunit;

namespace DawVcs.Infrastructure.Tests.FileSystem;

public sealed class AtomicFileWriterTests : IDisposable
{
    private readonly string _testDirectory;

    public AtomicFileWriterTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "dawvc_atomic_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort temp cleanup
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    [Trait("Requirement", "FR-OBJ-004")]
    public async Task WriteAtomicAsync_NewFile_CreatesFileWithExactContent()
    {
        // Arrange
        var targetFile = Path.Combine(_testDirectory, "new_object.bin");
        var expectedBytes = Encoding.UTF8.GetBytes("Brand new object content.");

        // Act
        using (var sourceStream = new MemoryStream(expectedBytes))
        {
            await AtomicFileWriter.WriteAtomicAsync(targetFile, sourceStream);
        }

        // Assert
        File.Exists(targetFile).Should().BeTrue();
        var actualBytes = await File.ReadAllBytesAsync(targetFile);
        actualBytes.Should().Equal(expectedBytes);
    }

    [Fact]
    [Trait("Requirement", "FR-OBJ-004")]
    [Trait("Requirement", "NFR-INT-004")]
    public async Task WriteAtomicAsync_ExistingFile_AtomicallyReplacesContent()
    {
        // Arrange
        var targetFile = Path.Combine(_testDirectory, "existing_object.bin");
        var initialBytes = Encoding.UTF8.GetBytes("Initial content version 1.");
        var updatedBytes = Encoding.UTF8.GetBytes("Updated content version 2 with different length.");

        await File.WriteAllBytesAsync(targetFile, initialBytes);

        // Act
        using (var sourceStream = new MemoryStream(updatedBytes))
        {
            await AtomicFileWriter.WriteAtomicAsync(targetFile, sourceStream);
        }

        // Assert
        var actualBytes = await File.ReadAllBytesAsync(targetFile);
        actualBytes.Should().Equal(updatedBytes);
    }

    [Fact]
    [Trait("Requirement", "FR-OBJ-010")]
    [Trait("Requirement", "NFR-INT-004")]
    public async Task WriteAtomicAsync_FailureDuringWrite_PreservesExistingFileAndCleansTemp()
    {
        // Arrange
        var targetFile = Path.Combine(_testDirectory, "critical_file.bin");
        var originalBytes = Encoding.UTF8.GetBytes("Original uncorrupted bytes.");
        await File.WriteAllBytesAsync(targetFile, originalBytes);

        // Act: Simulate crash / failure during write action
        var act = async () =>
        {
            await AtomicFileWriter.WriteAtomicAsync(
                targetFile,
                async stream =>
                {
                    // Write partial data then throw
                    await stream.WriteAsync(Encoding.UTF8.GetBytes("Corrupt partial data"));
                    throw new IOException("Simulated disk error or process crash before atomic replace!");
                });
        };

        // Assert
        await act.Should().ThrowAsync<IOException>()
            .WithMessage("*Simulated disk error*");

        // Target file must remain byte-identical to original
        var targetBytes = await File.ReadAllBytesAsync(targetFile);
        targetBytes.Should().Equal(originalBytes);

        // No leftover temporary files in the directory
        var filesInDir = Directory.GetFiles(_testDirectory);
        filesInDir.Should().ContainSingle().Which.Should().Be(targetFile);
    }

    [Fact]
    [Trait("Requirement", "FR-OBJ-004")]
    public async Task WriteAtomicAsync_StreamingLargePayload_WritesWithoutCorruption()
    {
        // Arrange: 1 MB payload
        var targetFile = Path.Combine(_testDirectory, "large_blob.bin");
        var payload = new byte[1024 * 1024];
        new Random(12345).NextBytes(payload);

        // Act
        using (var sourceStream = new MemoryStream(payload))
        {
            await AtomicFileWriter.WriteAtomicAsync(targetFile, sourceStream);
        }

        // Assert
        var writtenBytes = await File.ReadAllBytesAsync(targetFile);
        writtenBytes.Should().Equal(payload);
    }
}
