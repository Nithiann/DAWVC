using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json.Nodes;

using DawVcs.Application.Checkouts;
using DawVcs.Application.Commits;
using DawVcs.Application.Exceptions;
using DawVcs.Application.Recovery;
using DawVcs.Application.Remotes;
using DawVcs.Domain.Artifacts;
using DawVcs.Domain.Common;
using DawVcs.Domain.Configuration;
using DawVcs.Domain.Entities;
using DawVcs.Domain.Repositories;
using DawVcs.Domain.Storage;
using DawVcs.Infrastructure.Remotes;
using DawVcs.Infrastructure.Repositories;
using DawVcs.Infrastructure.Storage;

using FluentAssertions;

using Xunit;

namespace DawVcs.EndToEndTests;

public sealed class DurableRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dawvc-recovery-" + Guid.NewGuid().ToString("N"));
    private CommitId _before;
    private CommitId _after;
    private static FileSystemRepositoryContext Context(string root) => new(root);
    private static WorkspaceRecoveryUseCase Recovery => new(Context);
    private string Transaction => Directory.GetDirectories(Path.Combine(_root, ".dawvc", "transactions")).Single();

    public DurableRecoveryTests()
    {
        Directory.CreateDirectory(_root);
        using var context = Context(_root);
        context.SaveConfig(new RepositoryConfig(RepositoryId.New(), "Recovery song", new ArtifactPath("Song.flp")));
        context.SetCurrentBranch(BranchName.Main);
    }

    private async Task SetupAsync(bool unborn = false)
    {
        using var context = Context(_root);
        _before = await StoreAsync(context, "before", null);
        _after = await StoreAsync(context, "after", _before);
        context.CreateBranch(BranchName.Parse("incoming"), _after);
        if (!unborn)
        {
            await context.UpdateBranchCommitAsync(BranchName.Main, _before);
            File.WriteAllText(Path.Combine(_root, "Song.flp"), "unsaved project");
            File.WriteAllText(Path.Combine(_root, "old.wav"), "unsaved recording");
        }
        using var staged = new MemoryStream("staged bytes"u8.ToArray());
        var hash = await context.ObjectStore.WriteBlobAsync(staged);
        foreach (var path in new[] { "first.wav", "second.wav" })
            await context.StagingIndex.StageEntryAsync(ArtifactEntry.Create(new ArtifactPath(path), hash, staged.Length));
    }

    [Theory]
    [InlineData("prepared")]
    [InlineData("file-applied:1")]
    [InlineData("file-applied:2")]
    [InlineData("file-applied:3")]
    [InlineData("refs-updated")]
    [InlineData("index-cleared")]
    public async Task KilledFastForward_RestoresExactPrePublicationContentRefAndStagedIntent(string checkpoint)
    {
        await SetupAsync();
        var before = Workspace();
        await KillAsync("publish", checkpoint);
        (await WorkspaceRecoveryUseCase.InspectAsync(_root)).Should().ContainSingle().Which.State.Should().Be("Prepared");
        await FluentActions.Awaiting(() => new MergeUseCase(Context, new CheckoutUseCase(Context)).ExecuteAsync(
            new MergeRequest(_root, "incoming", MergeSnapshotChoice.Local, "blocked"))).Should().ThrowAsync<WorkspaceRecoveryRequiredException>();
        (await Recovery.RollbackAsync(_root)).RolledBack.Should().Be(1);
        Workspace().Should().BeEquivalentTo(before);
        await AssertOriginalAsync();
        (await Recovery.RollbackAsync(_root)).Should().Be(new WorkspaceRecoveryResult(0, 0));
    }

    [Theory]
    [InlineData("file-applied:1")]
    [InlineData("refs-updated")]
    [InlineData("index-cleared")]
    public async Task KilledSwitch_RestoresHeadAndDirtyFilesWithoutDeletingEitherHistory(string checkpoint)
    {
        await SetupAsync();
        var before = Workspace();
        await KillAsync("switch", checkpoint);
        await Recovery.RollbackAsync(_root);
        Workspace().Should().BeEquivalentTo(before);
        await AssertOriginalAsync();
        using var context = Context(_root);
        context.GetBranchCommit(BranchName.Parse("incoming")).Should().Be(_after);
    }

    [Theory]
    [InlineData("publish")]
    [InlineData("switch")]
    public async Task KilledAfterDurableCommit_PreservesPublishedVersion(string mode)
    {
        await SetupAsync();
        await KillAsync(mode, "committed");
        var published = Workspace();
        (await Recovery.RollbackAsync(_root)).CommittedCleaned.Should().Be(1);
        Workspace().Should().BeEquivalentTo(published);
        File.ReadAllText(Path.Combine(_root, "Song.flp")).Should().Be("after");
        using var context = Context(_root);
        context.GetCurrentBranch().Value.Should().Be(mode == "switch" ? "incoming" : "main");
        context.GetBranchCommit(context.GetCurrentBranch()).Should().Be(_after);
        (await context.StagingIndex.GetStagedEntriesAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("restored-file")]
    [InlineData("restored-refs")]
    [InlineData("restored-stage-entry")]
    [InlineData("terminal-moved")]
    public async Task KilledRecovery_IsRestartableIncludingPartiallyRestoredIndex(string checkpoint)
    {
        await SetupAsync();
        var before = Workspace();
        await KillAsync("publish", "index-cleared");
        await KillAsync("recover", checkpoint);
        await Recovery.RollbackAsync(_root);
        Workspace().Should().BeEquivalentTo(before);
        await AssertOriginalAsync();
    }

    [Fact]
    public async Task UnbornBranch_RecoversToMissingRefAndOriginalStagedEntries()
    {
        await SetupAsync(unborn: true);
        await KillAsync("publish", "index-cleared");
        await Recovery.RollbackAsync(_root);
        using var context = Context(_root);
        context.GetCurrentBranch().Should().Be(BranchName.Main);
        context.GetBranchCommit(BranchName.Main).Should().BeNull();
        File.Exists(Path.Combine(_root, "Song.flp")).Should().BeFalse();
        (await context.StagingIndex.GetStagedEntriesAsync()).Should().HaveCount(2);
    }

    [Theory]
    [InlineData("file")]
    [InlineData("ref")]
    [InlineData("head")]
    [InlineData("staged")]
    [InlineData("backup")]
    [InlineData("schema")]
    [InlineData("path")]
    public async Task UnexpectedEditsOrBadRecoveryData_FailBeforeAnyRecoveryWrites(string kind)
    {
        await SetupAsync();
        await KillAsync("publish", "index-cleared");
        using (var context = Context(_root))
        {
            if (kind == "file") File.WriteAllText(Path.Combine(_root, "Song.flp"), "new user work");
            if (kind == "ref") await context.UpdateBranchCommitAsync(BranchName.Main, CommitId.Parse(new string('a', 64)));
            if (kind == "head") context.SetCurrentBranch(BranchName.Parse("unexpected"));
            if (kind == "staged") await context.StagingIndex.StageEntryAsync(ArtifactEntry.Create(new ArtifactPath("unexpected.wav"), _before.Value, 1));
        }
        if (kind == "backup") File.WriteAllText(Path.Combine(Transaction, "files", "00000"), "corrupted backup");
        if (kind is "schema" or "path")
        {
            var path = Path.Combine(Transaction, "manifest.json");
            var manifest = JsonNode.Parse(File.ReadAllText(path))!;
            if (kind == "schema") manifest["SchemaVersion"] = 999;
            else manifest["Files"]![0]!["Path"] = "../outside";
            File.WriteAllText(path, manifest.ToJsonString());
        }
        var before = Workspace();
        var head = File.ReadAllBytes(Path.Combine(_root, ".dawvc", "HEAD"));
        var refs = File.ReadAllBytes(Path.Combine(_root, ".dawvc", "refs", "heads", "main"));
        await FluentActions.Awaiting(() => Recovery.RollbackAsync(_root)).Should().ThrowAsync<WorkspaceRecoveryRequiredException>();
        Workspace().Should().BeEquivalentTo(before);
        File.ReadAllBytes(Path.Combine(_root, ".dawvc", "HEAD")).Should().Equal(head);
        File.ReadAllBytes(Path.Combine(_root, ".dawvc", "refs", "heads", "main")).Should().Equal(refs);
        WorkspaceRecoveryGuard.HasPending(_root).Should().BeTrue();
    }

    [Fact]
    public async Task OwnershipLock_PreventsRecoveringAnActivePublisher()
    {
        await SetupAsync();
        await KillAsync("publish", "prepared");
        using var active = new FileStream(Path.Combine(_root, ".dawvc", "workspace-recovery.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        await FluentActions.Awaiting(() => Recovery.RollbackAsync(_root)).Should().ThrowAsync<WorkspaceRecoveryRequiredException>();
        WorkspaceRecoveryGuard.HasPending(_root).Should().BeTrue();
    }

    [Fact]
    public async Task Cli_InspectionIsReadOnlyAndMutationsAreBlockedUntilRecovery()
    {
        await SetupAsync();
        await KillAsync("publish", "refs-updated");
        var before = Workspace();
        (await CliAsync("recover", "--dir", _root)).Should().Be(0);
        Workspace().Should().BeEquivalentTo(before);
        (await CliAsync("commit", "-m", "blocked", "--dir", _root)).Should().Be(10);
        (await CliAsync("add", "Song.flp", "--dir", _root)).Should().Be(10);
        (await CliAsync("recover", "--rollback", "--dir", _root)).Should().Be(0);
        await AssertOriginalAsync();
    }

    [Fact]
    public async Task MetadataOnlyPublication_StillRestoresRefAndStaging()
    {
        await SetupAsync();
        using (var context = Context(_root))
        {
            File.WriteAllText(Path.Combine(_root, "Song.flp"), "before");
            File.WriteAllText(Path.Combine(_root, "old.wav"), "before");
            var old = (await context.LoadCommitAsync(_before))!;
            var next = new Commit([_before], old.SnapshotId, "test", DateTimeOffset.UtcNow, "same snapshot, new history");
            await context.ObjectStore.WriteObjectAsync(ObjectType.Commit, next.ToCanonicalBytes());
            await context.UpdateBranchCommitAsync(BranchName.Parse("incoming"), next.Id);
        }
        var before = Workspace();
        await KillAsync("publish", "refs-updated");
        (await WorkspaceRecoveryUseCase.InspectAsync(_root)).Single().Files.Should().Be(0);
        await Recovery.RollbackAsync(_root);
        Workspace().Should().BeEquivalentTo(before);
        await AssertOriginalAsync();
    }

    [Fact]
    public async Task CleanupInterruptedAfterCommit_DoesNotLeaveHalfDeletedActiveMetadata()
    {
        await SetupAsync();
        await KillAsync("publish", "terminal-moved");
        WorkspaceRecoveryGuard.HasPending(_root).Should().BeFalse();
        (await Recovery.RollbackAsync(_root)).Should().Be(new WorkspaceRecoveryResult(0, 0));
        using var context = Context(_root);
        context.GetBranchCommit(BranchName.Main).Should().Be(_after);
        File.ReadAllText(Path.Combine(_root, "Song.flp")).Should().Be("after");
    }

    [Fact]
    public async Task ExternalInspectionRestore_IsAllowedButCannotWriteInsidePendingWorkspace()
    {
        await SetupAsync();
        await KillAsync("publish", "file-applied:1");
        var before = Workspace();
        await FluentActions.Awaiting(() => new CheckoutUseCase(Context).ExecuteAsync(
            new CheckoutRequest(_root, _before.ToString(), Path.Combine(_root, "inside"))))
            .Should().ThrowAsync<WorkspaceRecoveryRequiredException>();
        var external = _root + "-review";
        try
        {
            await new CheckoutUseCase(Context).ExecuteAsync(new CheckoutRequest(_root, _before.ToString(), external));
            File.ReadAllText(Path.Combine(external, "Song.flp")).Should().Be("before");
            Workspace().Should().BeEquivalentTo(before);
            WorkspaceRecoveryGuard.HasPending(_root).Should().BeTrue();
        }
        finally { if (Directory.Exists(external)) Directory.Delete(external, recursive: true); }
    }

    [Fact]
    public async Task CommittedTransaction_WithLaterEditsIsNotRolledBackOrDiscarded()
    {
        await SetupAsync();
        await KillAsync("publish", "committed");
        File.WriteAllText(Path.Combine(_root, "Song.flp"), "new work after commit");
        var before = Workspace();
        await FluentActions.Awaiting(() => Recovery.RollbackAsync(_root)).Should().ThrowAsync<WorkspaceRecoveryRequiredException>();
        Workspace().Should().BeEquivalentTo(before);
        using var context = Context(_root);
        context.GetBranchCommit(BranchName.Main).Should().Be(_after);
        WorkspaceRecoveryGuard.HasPending(_root).Should().BeTrue();
    }

    [Fact]
    public async Task MultiplePendingTransactions_AreNeverAutomaticallyOrdered()
    {
        await SetupAsync();
        await KillAsync("publish", "prepared");
        var extra = Path.Combine(_root, ".dawvc", "transactions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extra);
        var before = Workspace();
        await FluentActions.Awaiting(() => Recovery.RollbackAsync(_root)).Should().ThrowAsync<WorkspaceRecoveryRequiredException>();
        Workspace().Should().BeEquivalentTo(before);
        Directory.GetDirectories(Path.Combine(_root, ".dawvc", "transactions")).Should().HaveCount(2);
    }

    [Fact]
    public async Task CorruptUnbornRef_IsNotMistakenForAnAbsentRef()
    {
        await SetupAsync(unborn: true);
        await KillAsync("publish", "refs-updated");
        var reference = Path.Combine(_root, ".dawvc", "refs", "heads", "main");
        File.WriteAllText(reference, "unexpected ref contents");
        var before = Workspace();
        await FluentActions.Awaiting(() => Recovery.RollbackAsync(_root)).Should().ThrowAsync<WorkspaceRecoveryRequiredException>();
        File.ReadAllText(reference).Should().Be("unexpected ref contents");
        Workspace().Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task EditsAfterPreflight_AreRecheckedBeforeOverwritingThatFile()
    {
        await SetupAsync();
        await KillAsync("publish", "index-cleared");
        var recovery = new WorkspaceRecoveryUseCase(Context, point =>
        {
            if (point == "recovery-preflighted") File.WriteAllText(Path.Combine(_root, "Song.flp"), "concurrent edit");
        });
        await FluentActions.Awaiting(() => recovery.RollbackAsync(_root)).Should().ThrowAsync<WorkspaceRecoveryRequiredException>();
        File.ReadAllText(Path.Combine(_root, "Song.flp")).Should().Be("concurrent edit");
        WorkspaceRecoveryGuard.HasPending(_root).Should().BeTrue();
    }

    [Fact]
    public async Task KillDuringPreparation_LeavesPrivateUnpublishedBackupsAndNoWorkspaceMutation()
    {
        await SetupAsync();
        var before = Workspace();
        await KillAsync("publish", "backup-prepared");
        Workspace().Should().BeEquivalentTo(before);
        (await WorkspaceRecoveryUseCase.InspectAsync(_root)).Should().BeEmpty();
        await AssertOriginalAsync();
        var preparation = Directory.GetDirectories(Path.Combine(_root, ".dawvc", "transaction-preparation")).Single();
        if (!OperatingSystem.IsWindows())
        {
            var access = File.GetUnixFileMode(preparation);
            (access & (UnixFileMode.GroupRead | UnixFileMode.OtherRead | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)).Should().Be(0);
        }
        else
        {
            using var identity = WindowsIdentity.GetCurrent();
            var security = new DirectoryInfo(preparation).GetAccessControl();
            security.AreAccessRulesProtected.Should().BeTrue();
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
            rules.Should().ContainSingle();
            rules[0].IdentityReference.Should().Be(identity.User);
            rules[0].AccessControlType.Should().Be(AccessControlType.Allow);
        }
        using var context = Context(_root);
        await new CheckoutUseCase(Context).ExecuteFastForwardAsync(new FastForwardTarget(_root, BranchName.Main, _before, _after), true);
        context.GetBranchCommit(BranchName.Main).Should().Be(_after);
        WorkspaceRecoveryGuard.HasPending(_root).Should().BeFalse();
    }

    [Fact]
    public async Task ForceClone_CannotOverwritePendingDestinationConfigurationOrHead()
    {
        await SetupAsync();
        await KillAsync("publish", "refs-updated");
        var configuration = File.ReadAllBytes(Path.Combine(_root, "dawvc.yaml"));
        var head = File.ReadAllBytes(Path.Combine(_root, ".dawvc", "HEAD"));
        var source = _root + "-source";
        try
        {
            Directory.CreateDirectory(source);
            using (var context = Context(source))
            {
                context.SaveConfig(new RepositoryConfig(RepositoryId.New(), "Other project", new ArtifactPath("Other.flp")));
                context.SetCurrentBranch(BranchName.Main);
            }
            var factory = new DefaultRemoteTransportFactory();
            var clone = new CloneUseCase(Context, factory, new FetchUseCase(Context, factory, path => new QuarantineObjectStore(path)), new CheckoutUseCase(Context));
            await FluentActions.Awaiting(() => clone.ExecuteAsync(new CloneRequest(source, _root, Force: true)))
                .Should().ThrowAsync<WorkspaceRecoveryRequiredException>();
            File.ReadAllBytes(Path.Combine(_root, "dawvc.yaml")).Should().Equal(configuration);
            File.ReadAllBytes(Path.Combine(_root, ".dawvc", "HEAD")).Should().Equal(head);
        }
        finally { if (Directory.Exists(source)) Directory.Delete(source, recursive: true); }
    }

    private async Task AssertOriginalAsync()
    {
        using var context = Context(_root);
        context.GetCurrentBranch().Should().Be(BranchName.Main);
        context.GetBranchCommit(BranchName.Main).Should().Be(_before);
        var entries = await context.StagingIndex.GetStagedEntriesAsync();
        entries.Select(e => e.Path.Value).Should().Equal("first.wav", "second.wav");
        foreach (var entry in entries)
            (await context.ObjectStore.ReadObjectPayloadAsync(entry.Hash)).Should().Equal("staged bytes"u8.ToArray());
        WorkspaceRecoveryGuard.HasPending(_root).Should().BeFalse();
    }

    private async Task KillAsync(string mode, string checkpoint)
    {
        using var process = Start(typeof(DawVcs.RecoveryCrashHost.Program).Assembly.Location, [mode, _root, checkpoint]);
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) { process.Kill(true); process.WaitForExit(); } }
        process.ExitCode.Should().NotBe(0, await output + await error);
        File.ReadAllText(Path.Combine(_root, ".dawvc", "killed-at")).Should().Be(checkpoint);
    }

    private static Process Start(string assembly, string[] args)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(assembly);
        foreach (var arg in args) start.ArgumentList.Add(arg);
        var process = new Process { StartInfo = start }; process.Start().Should().BeTrue(); return process;
    }

    private static async Task<int> CliAsync(params string[] args)
    {
        using var process = Start(typeof(DawVcs.Cli.Program).Assembly.Location, args);
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) { process.Kill(true); process.WaitForExit(); } }
        await output; await error; return process.ExitCode;
    }

    private static async Task<CommitId> StoreAsync(FileSystemRepositoryContext context, string label, CommitId? parent)
    {
        var entries = new List<ArtifactEntry>();
        foreach (var path in new[] { "Song.flp", label == "before" ? "old.wav" : "new.wav" })
        {
            using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(label));
            var hash = await context.ObjectStore.WriteBlobAsync(bytes);
            entries.Add(ArtifactEntry.Create(new ArtifactPath(path), hash, bytes.Length));
        }
        var snapshot = new ProjectSnapshot(new ProjectArtifact("Synthetic", new DirectoryArtifact(entries)), DateTimeOffset.UtcNow);
        await context.ObjectStore.WriteObjectAsync(ObjectType.Snapshot, snapshot.ToCanonicalBytes());
        var commit = new Commit(parent.HasValue ? [parent.Value] : [], snapshot.Id, "test", DateTimeOffset.UtcNow, label);
        await context.ObjectStore.WriteObjectAsync(ObjectType.Commit, commit.ToCanonicalBytes());
        return commit.Id;
    }

    private Dictionary<string, string> Workspace() => Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
        .Where(p => !Path.GetRelativePath(_root, p).StartsWith(".dawvc" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        .ToDictionary(p => Path.GetRelativePath(_root, p), p => Convert.ToBase64String(File.ReadAllBytes(p)));

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
