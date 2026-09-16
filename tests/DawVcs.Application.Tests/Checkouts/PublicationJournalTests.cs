using DawVcs.Application.Checkouts;
using DawVcs.Application.Exceptions;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Checkouts;

using FluentAssertions;

using Xunit;

namespace DawVcs.Application.Tests.Checkouts;

public sealed class PublicationJournalTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _targetDir;
    private readonly string _stagingDir;
    private readonly string _dotDawvcDir;

    public PublicationJournalTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "dawvc-journal-test-" + Guid.NewGuid().ToString("N"));
        _targetDir = Path.Combine(_tempDir, "workspace");
        _stagingDir = Path.Combine(_tempDir, "staging");
        _dotDawvcDir = Path.Combine(_tempDir, ".dawvc");

        Directory.CreateDirectory(_targetDir);
        Directory.CreateDirectory(_stagingDir);
        Directory.CreateDirectory(_dotDawvcDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    [Trait("Requirement", "FR-CHK-006")]
    public async Task ExecutePlanAsync_SuccessfulPlan_AppliesAllActionsCleanly()
    {
        // Arrange
        var fileA = Path.Combine(_targetDir, "FileA.txt");
        await File.WriteAllTextAsync(fileA, "Original A");

        var stagedA = Path.Combine(_stagingDir, "FileA.txt");
        await File.WriteAllTextAsync(stagedA, "New A");

        var fileB = Path.Combine(_targetDir, "FileB.txt");
        await File.WriteAllTextAsync(fileB, "Original B (to delete)");

        var stagedC = Path.Combine(_stagingDir, "FileC.txt");
        await File.WriteAllTextAsync(stagedC, "New C (to create)");

        var plan = new CheckoutPlan(
            [
                new CheckoutAction(new ArtifactPath("FileA.txt"), CheckoutActionKind.Replace),
                new CheckoutAction(new ArtifactPath("FileB.txt"), CheckoutActionKind.Delete),
                new CheckoutAction(new ArtifactPath("FileC.txt"), CheckoutActionKind.Create)
            ],
            []);

        // Act
        await using (var journal = new PublicationJournal(_targetDir, _dotDawvcDir))
        {
            var modified = await journal.ExecutePlanAsync(_stagingDir, plan);
            modified.Should().HaveCount(3);
        }

        // Assert
        (await File.ReadAllTextAsync(fileA)).Should().Be("New A");
        File.Exists(fileB).Should().BeFalse();
        (await File.ReadAllTextAsync(Path.Combine(_targetDir, "FileC.txt"))).Should().Be("New C (to create)");
    }

    [Fact]
    [Trait("Requirement", "FR-CHK-006")]
    public async Task ExecutePlanAsync_FailureMidway_RollsBackAllPreviousActionsToOriginalState()
    {
        // Arrange:
        // Action 1: Replace FileA.txt (succeeds)
        // Action 2: Delete FileB.txt (succeeds)
        // Action 3: Create FileC.txt (fails because staged file is missing!)
        var fileA = Path.Combine(_targetDir, "FileA.txt");
        await File.WriteAllTextAsync(fileA, "Original A content");

        var stagedA = Path.Combine(_stagingDir, "FileA.txt");
        await File.WriteAllTextAsync(stagedA, "New A content (should be rolled back)");

        var fileB = Path.Combine(_targetDir, "FileB.txt");
        await File.WriteAllTextAsync(fileB, "Original B content (should be restored)");

        // Notice: FileC.txt is NOT created in _stagingDir, which will cause FileNotFoundException during Action 3
        var plan = new CheckoutPlan(
            [
                new CheckoutAction(new ArtifactPath("FileA.txt"), CheckoutActionKind.Replace),
                new CheckoutAction(new ArtifactPath("FileB.txt"), CheckoutActionKind.Delete),
                new CheckoutAction(new ArtifactPath("FileC.txt"), CheckoutActionKind.Create)
            ],
            []);

        // Act
        await using (var journal = new PublicationJournal(_targetDir, _dotDawvcDir))
        {
            var act = () => journal.ExecutePlanAsync(_stagingDir, plan);
            var ex = await act.Should().ThrowAsync<CheckoutStagingException>();
            ex.Which.Message.Should().Contain("rolled back");
        }

        // Assert: Entire workspace must be rolled back to pre-checkout state!
        (await File.ReadAllTextAsync(fileA)).Should().Be("Original A content", "replaced file must be restored from journal backup");
        File.Exists(fileB).Should().BeTrue("deleted file must be restored from journal backup");
        (await File.ReadAllTextAsync(fileB)).Should().Be("Original B content (should be restored)");
        File.Exists(Path.Combine(_targetDir, "FileC.txt")).Should().BeFalse("failed create file must not exist");
    }
}
