using System.CommandLine;
using System.Text.Json;

using DawVcs.Adapters.Abstractions;
using DawVcs.Adapters.FLStudio;
using DawVcs.Application.Adapters;
using DawVcs.Application.Branches;
using DawVcs.Application.Checkouts;
using DawVcs.Application.Commits;
using DawVcs.Application.Dependencies;
using DawVcs.Application.Diagnostics;
using DawVcs.Application.Exceptions;
using DawVcs.Application.Repositories;
using DawVcs.Application.Scanning;
using DawVcs.Application.Staging;
using DawVcs.Domain.Repositories;
using DawVcs.Infrastructure.Repositories;

using Microsoft.Extensions.DependencyInjection;

using Spectre.Console;

namespace DawVcs.Cli;

public static class Program
{
    private static readonly string[] MessageAliases = ["-m", "--message"];
    private static readonly string[] LimitAliases = ["-n", "--limit"];
    private static readonly string[] AllAliases = ["-A", "--all"];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<int> Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Func<string, IRepositoryContext>>(sp => dir => new FileSystemRepositoryContext(dir));
        services.AddSingleton<IDawAdapterRegistry>(sp => new DawAdapterRegistry([new FLStudioAdapter()]));
        services.AddTransient<InitRepositoryUseCase>();
        services.AddTransient<CommitUseCase>(sp => new CommitUseCase(
            sp.GetRequiredService<Func<string, IRepositoryContext>>(),
            sp.GetRequiredService<IDawAdapterRegistry>()));
        services.AddTransient<LogUseCase>();
        services.AddTransient<CheckoutRestoreUseCase>();
        services.AddTransient<CheckoutUseCase>();
        services.AddTransient<BindDependencyUseCase>();
        services.AddTransient<StatusUseCase>();
        services.AddTransient<AddUseCase>();
        services.AddTransient<ScanUseCase>();
        services.AddTransient<BranchUseCase>();
        services.AddTransient<SwitchUseCase>();
        services.AddTransient<DoctorUseCase>(sp => new DoctorUseCase(
            sp.GetRequiredService<Func<string, IRepositoryContext>>(),
            sp.GetRequiredService<IDawAdapterRegistry>()));
        services.AddTransient<FsckUseCase>(sp => new FsckUseCase(
            sp.GetRequiredService<Func<string, IRepositoryContext>>(),
            sp.GetRequiredService<IDawAdapterRegistry>()));

        using var serviceProvider = services.BuildServiceProvider();

        var rootCommand = new RootCommand("DAWVC — Version control and dependency management for DAWs");

        // --- dawvc init ---
        var initCommand = new Command("init", "Initialize a new DAWVC repository");
        var initDirOption = new Option<string?>("--dir", "Target directory to initialize (default: current directory)");
        var initNameOption = new Option<string?>("--name", "Custom project name");
        var initPrimaryOption = new Option<string?>("--primary", "Explicit path to the primary DAW project file");
        initCommand.AddOption(initDirOption);
        initCommand.AddOption(initNameOption);
        initCommand.AddOption(initPrimaryOption);

        initCommand.SetHandler(async (dir, name, primary) =>
        {
            try
            {
                var targetDir = string.IsNullOrWhiteSpace(dir) ? Directory.GetCurrentDirectory() : dir;
                var useCase = serviceProvider.GetRequiredService<InitRepositoryUseCase>();
                var result = await useCase.ExecuteAsync(new InitRequest(targetDir, name, primary));

                if (result.WasAlreadyInitialized)
                {
                    AnsiConsole.MarkupLine($"[yellow]![/] Existing DAWVC repository reinitialized in [bold]{Markup.Escape(targetDir)}[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[green]✓[/] Initialized empty DAWVC repository in [bold]{Markup.Escape(targetDir)}[/]");
                }

                AnsiConsole.MarkupLine($"  [dim]Repository ID:[/] {result.RepositoryId}");
                AnsiConsole.MarkupLine($"  [dim]Project Name:[/]  {Markup.Escape(result.ProjectName)}");
                AnsiConsole.MarkupLine($"  [dim]Primary File:[/]  [cyan]{Markup.Escape(result.PrimaryArtifact.Value)}[/]");
                AnsiConsole.MarkupLine($"  [dim]Branch:[/]        [yellow]{result.DefaultBranch.Value}[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.ExitCode = 1;
            }
        }, initDirOption, initNameOption, initPrimaryOption);

        // --- dawvc commit ---
        var commitCommand = new Command("commit", "Record changes to the repository");
        var commitMessageOption = new Option<string>(MessageAliases, "Commit message describing the changes") { IsRequired = true };
        var commitAuthorOption = new Option<string?>("--author", "Author name/identity");
        var commitDirOption = new Option<string?>("--dir", "Repository directory");
        var commitAllowIncompleteOption = new Option<bool>("--allow-incomplete", "Allow commit even if required bundle dependencies are missing (marks snapshot incomplete)");
        commitCommand.AddOption(commitMessageOption);
        commitCommand.AddOption(commitAuthorOption);
        commitCommand.AddOption(commitDirOption);
        commitCommand.AddOption(commitAllowIncompleteOption);

        commitCommand.SetHandler(async (message, author, dir, allowIncomplete) =>
        {
            try
            {
                var repoDir = ResolveRepoDirectory(dir);
                var useCase = serviceProvider.GetRequiredService<CommitUseCase>();
                var result = await useCase.ExecuteAsync(new CommitRequest(repoDir, message, author, allowIncomplete));

                AnsiConsole.MarkupLine($"[green]✓[/] ([yellow]{result.Branch.Value}[/] [bold]{result.CommitId.ToString()[..8]}[/]) {Markup.Escape(result.Message)}");
                AnsiConsole.MarkupLine($"  [dim]Author:[/]   {Markup.Escape(result.Author)}");
                AnsiConsole.MarkupLine($"  [dim]Snapshot:[/] {result.SnapshotId.ToString()[..8]}");
                if (!result.IsComplete)
                {
                    AnsiConsole.MarkupLine($"  [yellow]![/] [bold yellow]Incomplete snapshot:[/] {Markup.Escape(result.IncompleteReason ?? "Ontbrekende afhankelijkheden")}");
                }
            }
            catch (IncompleteDependencyException ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Error (Exit Code 5):[/] {Markup.Escape(ex.Message)}");
                AnsiConsole.MarkupLine("[yellow]Remediation:[/] Add the missing files to the project directory or use [bold]dawvc commit -m \"...\" --allow-incomplete[/]");
                Environment.ExitCode = 5;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("clean", StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(ex.Message)}[/]");
                Environment.ExitCode = 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.ExitCode = 1;
            }
        }, commitMessageOption, commitAuthorOption, commitDirOption, commitAllowIncompleteOption);

        // --- dawvc log ---
        var logCommand = new Command("log", "Show commit history");
        var logLimitOption = new Option<int?>(LimitAliases, "Number of commits to show");
        var logDirOption = new Option<string?>("--dir", "Repository directory");
        logCommand.AddOption(logLimitOption);
        logCommand.AddOption(logDirOption);

        logCommand.SetHandler(async (limit, dir) =>
        {
            try
            {
                var repoDir = ResolveRepoDirectory(dir);
                var useCase = serviceProvider.GetRequiredService<LogUseCase>();
                var result = await useCase.ExecuteAsync(new LogRequest(repoDir, limit));

                if (result.Entries.Count == 0)
                {
                    AnsiConsole.MarkupLine("[dim]No commits found on current branch.[/]");
                    return;
                }

                foreach (var entry in result.Entries)
                {
                    AnsiConsole.MarkupLine($"[yellow]commit {entry.Id}[/]");
                    AnsiConsole.MarkupLine($"Author:    {Markup.Escape(entry.Author)}");
                    AnsiConsole.MarkupLine($"Date:      {entry.Timestamp:yyyy-MM-dd HH:mm:ss zzz}");
                    AnsiConsole.MarkupLine($"Snapshot:  [dim]{entry.SnapshotId}[/]");
                    AnsiConsole.WriteLine();
                    AnsiConsole.WriteLine($"    {entry.Message}");
                    AnsiConsole.WriteLine();
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.ExitCode = 1;
            }
        }, logLimitOption, logDirOption);

        // --- dawvc checkout ---
        var checkoutCommand = new Command("checkout", "Checkout a commit or branch into the workspace or an external directory (FR-CHK-001..015)");
        var checkoutRefArg = new Argument<string>("reference", "Commit hash or branch name to checkout");
        var checkoutRestoreToOption = new Option<string?>("--restore-to", "Target directory to restore project artifacts into without touching active workspace");
        var checkoutForceOption = new Option<bool>("--force", "Force checkout on a dirty workspace after creating a full recovery copy");
        var checkoutDirOption = new Option<string?>("--dir", "Repository directory");
        checkoutCommand.AddArgument(checkoutRefArg);
        checkoutCommand.AddOption(checkoutRestoreToOption);
        checkoutCommand.AddOption(checkoutForceOption);
        checkoutCommand.AddOption(checkoutDirOption);

        checkoutCommand.SetHandler(async (reference, restoreTo, force, dir) =>
        {
            try
            {
                var repoDir = ResolveRepoDirectory(dir);
                var useCase = serviceProvider.GetRequiredService<CheckoutUseCase>();
                var result = await useCase.ExecuteAsync(new CheckoutRequest(repoDir, reference, restoreTo, force));

                if (!string.IsNullOrEmpty(result.RecoveryDirectory))
                {
                    AnsiConsole.MarkupLine($"[yellow]⚠ Recovery copy created:[/] [dim]{Markup.Escape(result.RecoveryDirectory)}[/]");
                }

                AnsiConsole.MarkupLine($"[green]✓[/] Successfully checked out snapshot [dim]{result.SnapshotId.ToString()[..8]}[/] to [cyan]{Markup.Escape(result.TargetDirectory)}[/]");
                foreach (var file in result.RestoredFiles)
                {
                    AnsiConsole.MarkupLine($"  [green]+[/] {Markup.Escape(file)}");
                }

                if (result.ManualInstructions is not null && result.ManualInstructions.Count > 0)
                {
                    AnsiConsole.MarkupLine("\n[bold yellow]Instructions & Next Steps:[/]");
                    foreach (var instruction in result.ManualInstructions)
                    {
                        AnsiConsole.MarkupLine($"  [dim]•[/] {Markup.Escape(instruction)}");
                    }
                }
            }
            catch (DirtyWorkspaceException ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.ExitCode = 7;
            }
            catch (IncompleteDependencyException ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.ExitCode = 5;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.ExitCode = 1;
            }
        }, checkoutRefArg, checkoutRestoreToOption, checkoutForceOption, checkoutDirOption);

        // --- dawvc bind ---
        var bindCommand = new Command("bind", "Bind a dependency to a local file or directory (FR-BND-006)");
        var bindDepIdArg = new Argument<string>("dependency-id", "The ID of the dependency to bind");
        var bindPathArg = new Argument<string>("path", "The local filesystem path to bind to");
        var bindDirOption = new Option<string?>("--dir", "Repository directory");
        bindCommand.AddArgument(bindDepIdArg);
        bindCommand.AddArgument(bindPathArg);
        bindCommand.AddOption(bindDirOption);

        bindCommand.SetHandler(async (depId, path, dir) =>
        {
            try
            {
                var repoDir = ResolveRepoDirectory(dir);
                var useCase = serviceProvider.GetRequiredService<BindDependencyUseCase>();
                var result = await useCase.ExecuteAsync(new BindDependencyRequest(repoDir, depId, path));
                var b = result.Binding;

                switch (b.Status)
                {
                    case Domain.Dependencies.BindingStatus.Verified:
                        AnsiConsole.MarkupLine($"[green]✓[/] Bound [cyan]{Markup.Escape(b.DependencyId.Value)}[/] to [cyan]{Markup.Escape(b.Locator)}[/] ([green]Verified[/])");
                        break;
                    case Domain.Dependencies.BindingStatus.Mismatch:
                        AnsiConsole.MarkupLine($"[yellow]⚠[/] Bound [cyan]{Markup.Escape(b.DependencyId.Value)}[/] to [cyan]{Markup.Escape(b.Locator)}[/] ([red]Mismatch[/] - content hash differs!)");
                        break;
                    case Domain.Dependencies.BindingStatus.Missing:
                        AnsiConsole.MarkupLine($"[red]✗[/] Bound [cyan]{Markup.Escape(b.DependencyId.Value)}[/] to [cyan]{Markup.Escape(b.Locator)}[/] ([red]Missing[/] - file not found!)");
                        break;
                    default:
                        AnsiConsole.MarkupLine($"[dim]?[/] Bound [cyan]{Markup.Escape(b.DependencyId.Value)}[/] to [cyan]{Markup.Escape(b.Locator)}[/] ([yellow]{b.Status}[/])");
                        break;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.ExitCode = 1;
            }
        }, bindDepIdArg, bindPathArg, bindDirOption);

        // --- dawvc status ---
        var statusCommand = new Command("status", "Show the working tree status");
        var statusDirOption = new Option<string?>("--dir", "Repository directory");
        statusCommand.AddOption(statusDirOption);

        statusCommand.SetHandler(async (dir) =>
        {
            try
            {
                var repoDir = ResolveRepoDirectory(dir);
                var useCase = serviceProvider.GetRequiredService<StatusUseCase>();
                var result = await useCase.ExecuteAsync(new StatusRequest(repoDir));
                var s = result.Status;

                AnsiConsole.MarkupLine($"On branch [yellow]{s.CurrentBranch.Value}[/]");
                if (!s.HeadCommit.HasValue)
                {
                    AnsiConsole.MarkupLine("\nNo commits yet\n");
                }

                if (s.HasStagedChanges)
                {
                    AnsiConsole.MarkupLine("\nChanges to be committed:");
                    AnsiConsole.MarkupLine("  [dim](use \"dawvc commit\" to record changes)[/]\n");
                    foreach (var item in s.Staged)
                    {
                        AnsiConsole.MarkupLine($"\t[green]staged:[/]   {Markup.Escape(item.Path.Value)}");
                    }
                }

                if (s.HasWorkingTreeModifications)
                {
                    AnsiConsole.MarkupLine("\nChanges not staged for commit:");
                    AnsiConsole.MarkupLine("  [dim](use \"dawvc add <file>...\" to update what will be committed)[/]\n");
                    foreach (var item in s.Modified)
                    {
                        AnsiConsole.MarkupLine($"\t[red]modified:[/] {Markup.Escape(item.Path.Value)}");
                    }
                    foreach (var item in s.Missing)
                    {
                        AnsiConsole.MarkupLine($"\t[red]deleted:[/]  {Markup.Escape(item.Path.Value)}");
                    }
                }

                if (s.Renamed.Count > 0)
                {
                    AnsiConsole.MarkupLine("\nRenamed files:");
                    foreach (var item in s.Renamed)
                    {
                        AnsiConsole.MarkupLine($"\t[cyan]renamed:[/]  {Markup.Escape(item.OldPath?.Value ?? string.Empty)} -> {Markup.Escape(item.Path.Value)}");
                    }
                }

                if (s.HasUntrackedAssets)
                {
                    AnsiConsole.MarkupLine("\nUntracked files:");
                    AnsiConsole.MarkupLine("  [dim](use \"dawvc add <file>...\" to include in what will be committed)[/]\n");
                    foreach (var item in s.Untracked)
                    {
                        AnsiConsole.MarkupLine($"\t[yellow]{Markup.Escape(item.Path.Value)}[/]");
                    }
                }

                if (s.IsClean)
                {
                    AnsiConsole.MarkupLine("nothing to commit, working tree clean");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.ExitCode = 1;
            }
        }, statusDirOption);

        // --- dawvc add ---
        var addCommand = new Command("add", "Add file contents to the staging area");
        var addPathsArg = new Argument<string[]>("paths", () => Array.Empty<string>(), "Files to add content from");
        var addAllOption = new Option<bool>(AllAliases, "Add all untracked and modified files to staging");
        var addDirOption = new Option<string?>("--dir", "Repository directory");
        addCommand.AddArgument(addPathsArg);
        addCommand.AddOption(addAllOption);
        addCommand.AddOption(addDirOption);

        addCommand.SetHandler(async (paths, all, dir) =>
        {
            try
            {
                var repoDir = ResolveRepoDirectory(dir);
                var useCase = serviceProvider.GetRequiredService<AddUseCase>();
                var result = await useCase.ExecuteAsync(new AddRequest(repoDir, paths, all));

                AnsiConsole.MarkupLine($"[green]✓[/] Staged [bold]{result.StagedPaths.Count}[/] file(s)");
                foreach (var path in result.StagedPaths)
                {
                    AnsiConsole.MarkupLine($"  [green]+[/] {Markup.Escape(path.Value)}");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.ExitCode = 1;
            }
        }, addPathsArg, addAllOption, addDirOption);

        // --- dawvc scan ---
        var scanCommand = new Command("scan", "Inspect and validate project files and detect DAW dependencies");
        var scanDirOption = new Option<string?>("--dir", "Working directory to scan (default: current directory)");
        var scanFileOption = new Option<string?>("--file", "Explicit path to the project file to scan");
        var scanTimeoutOption = new Option<int?>("--timeout", "Maximum scan timeout in seconds (default: 5)");
        scanCommand.AddOption(scanDirOption);
        scanCommand.AddOption(scanFileOption);
        scanCommand.AddOption(scanTimeoutOption);

        scanCommand.SetHandler(async (dir, file, timeoutSec) =>
        {
            try
            {
                var targetDir = string.IsNullOrWhiteSpace(dir) ? Directory.GetCurrentDirectory() : Path.GetFullPath(dir);
                var useCase = serviceProvider.GetRequiredService<ScanUseCase>();
                var timeout = timeoutSec.HasValue ? TimeSpan.FromSeconds(timeoutSec.Value) : ScanUseCase.DefaultTimeout;

                var result = await useCase.ExecuteAsync(new ScanRequest(targetDir, file, timeout));
                var detection = result.Detection;

                string statusColor = detection.Status switch
                {
                    ProjectDetectionStatus.Valid => "green",
                    ProjectDetectionStatus.Suspicious => "yellow",
                    ProjectDetectionStatus.Unsupported => "darkorange",
                    ProjectDetectionStatus.Invalid => "red",
                    _ => "grey"
                };

                AnsiConsole.WriteLine();
                var rule = new Rule($"[bold]Project Scan — {Markup.Escape(result.PrimaryDawName ?? "Unknown DAW")}[/]");
                rule.LeftJustified();
                AnsiConsole.Write(rule);

                var table = new Table().Border(TableBorder.Rounded);
                table.AddColumn("Property");
                table.AddColumn("Value");

                table.AddRow("Artifact", Markup.Escape(result.RelativeArtifactPath.Value));
                table.AddRow("Status", $"[{statusColor}]{detection.Status}[/]");
                table.AddRow("Confidence", $"{Math.Round(detection.Confidence * 100, 1)}%");
                if (!string.IsNullOrEmpty(detection.DetectedVersion))
                {
                    table.AddRow("Version", Markup.Escape(detection.DetectedVersion));
                }
                if (detection.Metadata.TryGetValue("ReleaseName", out var release))
                {
                    table.AddRow("Release", Markup.Escape(release));
                }
                if (detection.Metadata.TryGetValue("ProjectTitle", out var title))
                {
                    table.AddRow("Title", Markup.Escape(title));
                }
                if (detection.Metadata.TryGetValue("ChannelCount", out var channels))
                {
                    table.AddRow("Channels", Markup.Escape(channels));
                }
                if (detection.Metadata.TryGetValue("Ppq", out var ppq))
                {
                    table.AddRow("PPQ", Markup.Escape(ppq));
                }
                if (detection.Metadata.TryGetValue("TempoBpm", out var tempo))
                {
                    table.AddRow("Tempo", $"{Markup.Escape(tempo)} BPM");
                }
                table.AddRow("Duration", $"{result.Duration.TotalMilliseconds:F1} ms");

                AnsiConsole.Write(table);

                if (detection.Metadata.TryGetValue("SamplePaths", out var samplePaths) && !string.IsNullOrWhiteSpace(samplePaths))
                {
                    var samples = samplePaths.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    var tree = new Tree($"[bold]Referenced Samples ({samples.Length})[/]");
                    foreach (var s in samples)
                    {
                        tree.AddNode(Markup.Escape(s));
                    }
                    AnsiConsole.Write(tree);
                }

                if (detection.Metadata.TryGetValue("PluginNames", out var pluginNames) && !string.IsNullOrWhiteSpace(pluginNames))
                {
                    var plugins = pluginNames.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    var tree = new Tree($"[bold]Referenced Plugins ({plugins.Length})[/]");
                    foreach (var p in plugins)
                    {
                        tree.AddNode(Markup.Escape(p));
                    }
                    AnsiConsole.Write(tree);
                }

                if (detection.Findings.Count > 0)
                {
                    AnsiConsole.MarkupLine("\n[bold]Findings:[/]");
                    foreach (var finding in detection.Findings)
                    {
                        AnsiConsole.MarkupLine($"  • {Markup.Escape(finding)}");
                    }
                }

                if (result.RequiresOpaqueFallback)
                {
                    AnsiConsole.MarkupLine("\n[yellow]Note: This project will be safely tracked as an opaque artifact without blocking commits (FR-FLP-008).[/]");
                }
                AnsiConsole.WriteLine();
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.ExitCode = 1;
            }
        }, scanDirOption, scanFileOption, scanTimeoutOption);

        // --- dawvc branch ---
        var branchCommand = new Command("branch", "List, create, or delete branches (FR-BRA-001..003)");
        var branchNameArg = new Argument<string?>("name", () => null, "Branch name to create");
        var branchStartArg = new Argument<string?>("start-point", () => null, "Starting commit or reference");
        var branchDeleteOption = new Option<string?>(["-d", "--delete"], "Delete a branch");
        var branchForceDeleteOption = new Option<string?>(["-D", "--force-delete"], "Force delete a branch");
        var branchDirOption = new Option<string?>("--dir", "Repository directory");

        branchCommand.AddArgument(branchNameArg);
        branchCommand.AddArgument(branchStartArg);
        branchCommand.AddOption(branchDeleteOption);
        branchCommand.AddOption(branchForceDeleteOption);
        branchCommand.AddOption(branchDirOption);

        branchCommand.SetHandler(async (name, startPoint, deleteName, forceDeleteName, dir) =>
        {
            try
            {
                var repoDir = ResolveRepoDirectory(dir);
                var useCase = serviceProvider.GetRequiredService<BranchUseCase>();

                var branchToDelete = forceDeleteName ?? deleteName;
                if (!string.IsNullOrWhiteSpace(branchToDelete))
                {
                    await useCase.DeleteAsync(new BranchDeleteRequest(repoDir, branchToDelete, forceDeleteName != null));
                    AnsiConsole.MarkupLine($"[green]✓[/] Deleted branch [yellow]{Markup.Escape(branchToDelete)}[/]");
                    return;
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    var created = await useCase.CreateAsync(new BranchCreateRequest(repoDir, name, startPoint));
                    AnsiConsole.MarkupLine($"[green]✓[/] Created branch [yellow]{Markup.Escape(created.Name.Value)}[/] at [bold]{(created.CommitId?.ToString()[..8] ?? "HEAD")}[/]");
                    return;
                }

                // List branches
                var branches = await useCase.ListAsync(repoDir);
                foreach (var b in branches)
                {
                    var commitShort = b.CommitId.HasValue ? b.CommitId.Value.ToString()[..8] : "(no commits)";
                    if (b.IsCurrent)
                    {
                        AnsiConsole.MarkupLine($"* [bold green]{Markup.Escape(b.Name.Value)}[/] [dim]{commitShort}[/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"  {Markup.Escape(b.Name.Value)} [dim]{commitShort}[/]");
                    }
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.ExitCode = 1;
            }
        }, branchNameArg, branchStartArg, branchDeleteOption, branchForceDeleteOption, branchDirOption);

        // --- dawvc switch ---
        var switchCommand = new Command("switch", "Switch branches or restore working tree files (FR-BRA-004..007)");
        var switchBranchArg = new Argument<string>("branch", "Branch name to switch to");
        var switchCreateOption = new Option<bool>(["-c", "-b", "--create"], "Create and switch to a new branch");
        var switchStartPointOption = new Option<string?>("--start-point", "Starting commit when creating a new branch");
        var switchForceOption = new Option<bool>(["-f", "--force"], "Proceed and create a recovery copy if workspace is dirty");
        var switchDirOption = new Option<string?>("--dir", "Repository directory");

        switchCommand.AddArgument(switchBranchArg);
        switchCommand.AddOption(switchCreateOption);
        switchCommand.AddOption(switchStartPointOption);
        switchCommand.AddOption(switchForceOption);
        switchCommand.AddOption(switchDirOption);

        switchCommand.SetHandler(async (branch, create, startPoint, force, dir) =>
        {
            try
            {
                var repoDir = ResolveRepoDirectory(dir);
                var useCase = serviceProvider.GetRequiredService<SwitchUseCase>();
                var result = await useCase.ExecuteAsync(new SwitchRequest(repoDir, branch, create, startPoint, force));

                if (result.AlreadyOnBranch)
                {
                    AnsiConsole.MarkupLine($"Already on '[yellow]{Markup.Escape(result.Branch.Value)}[/]'");
                    return;
                }

                if (result.CreatedNewBranch)
                {
                    AnsiConsole.MarkupLine($"[green]✓[/] Switched to a new branch '[yellow]{Markup.Escape(result.Branch.Value)}[/]'");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[green]✓[/] Switched to branch '[yellow]{Markup.Escape(result.Branch.Value)}[/]'");
                }

                if (!string.IsNullOrEmpty(result.RecoveryDirectory))
                {
                    AnsiConsole.MarkupLine($"  [yellow]⚠ Recovery copy created:[/] [dim]{Markup.Escape(result.RecoveryDirectory)}[/]");
                }
            }
            catch (DirtyWorkspaceException ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Error (Exit Code 7):[/] {Markup.Escape(ex.Message)}");
                AnsiConsole.MarkupLine("[yellow]Remediation:[/] Commit or stage your changes, or use [bold]dawvc switch --force <branch>[/] to create a recovery copy.");
                Environment.ExitCode = 7;
            }
            catch (CheckoutStagingException ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Error (Exit Code 7):[/] {Markup.Escape(ex.Message)}");
                AnsiConsole.MarkupLine("[yellow]Remediation:[/] Commit or stage your changes, or use [bold]dawvc switch --force <branch>[/] to create a recovery copy.");
                Environment.ExitCode = 7;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.ExitCode = 1;
            }
        }, switchBranchArg, switchCreateOption, switchStartPointOption, switchForceOption, switchDirOption);

        // --- dawvc doctor ---
        var doctorCommand = new Command("doctor", "Evaluate repository health, dependency reproducibility, and DAW environment (FR-DOC-001..010)");
        var doctorJsonOption = new Option<bool>("--json", "Output diagnostics report as JSON");
        var doctorDirOption = new Option<string?>("--dir", "Repository directory");

        doctorCommand.AddOption(doctorJsonOption);
        doctorCommand.AddOption(doctorDirOption);

        doctorCommand.SetHandler(async (json, dir) =>
        {
            try
            {
                var repoDir = ResolveRepoDirectory(dir);
                var useCase = serviceProvider.GetRequiredService<DoctorUseCase>();
                var report = await useCase.ExecuteAsync(new DoctorRequest(repoDir));

                if (json)
                {
                    var jsonStr = JsonSerializer.Serialize(report, JsonOptions);
                    Console.WriteLine(jsonStr);
                }
                else
                {
                    AnsiConsole.MarkupLine("\n[bold]DAWVC Doctor Diagnostic Report[/]\n");

                    // 1. Artifacts
                    AnsiConsole.MarkupLine("[bold underline]1. Artifact Integrity[/]");
                    foreach (var art in report.ArtifactHealth)
                    {
                        var icon = art.Status switch
                        {
                            HealthStatus.Healthy => "[green]✓[/]",
                            HealthStatus.Warning => "[yellow]⚠[/]",
                            _ => "[red]✗[/]"
                        };
                        AnsiConsole.MarkupLine($"  {icon} [bold]{Markup.Escape(art.Path)}[/] ({Markup.Escape(art.DawName)} {Markup.Escape(art.DetectedVersion ?? "Unknown")})");
                        foreach (var finding in art.Findings)
                        {
                            AnsiConsole.MarkupLine($"    [dim]•[/] {Markup.Escape(finding)}");
                        }
                    }

                    // 2. Dependencies & Reproducibility
                    AnsiConsole.MarkupLine("\n[bold underline]2. Dependency Health & Reproducibility[/]");
                    AnsiConsole.MarkupLine($"  Reproducibility Score: [bold]{report.ReproducibilityScore:F1}%[/]");

                    if (report.DependencyHealth.Count > 0)
                    {
                        var table = new Table();
                        table.Border(TableBorder.Rounded);
                        table.AddColumn("Type");
                        table.AddColumn("Name");
                        table.AddColumn("Requirement");
                        table.AddColumn("Status");

                        foreach (var dep in report.DependencyHealth)
                        {
                            var statusMarkup = dep.Status switch
                            {
                                Domain.Dependencies.BindingStatus.Verified => "[green]Verified[/]",
                                Domain.Dependencies.BindingStatus.Mismatch => "[red]Mismatch[/]",
                                Domain.Dependencies.BindingStatus.Missing => "[red]Missing[/]",
                                _ => "[yellow]Unresolved[/]"
                            };
                            table.AddRow(dep.Category, Markup.Escape(dep.Name), dep.Requirement.ToString(), statusMarkup);
                        }
                        AnsiConsole.Write(table);
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("  [dim]No external dependencies discovered.[/]");
                    }

                    // 3. Environment
                    AnsiConsole.MarkupLine("\n[bold underline]3. Environment & DAW Installation[/]");
                    AnsiConsole.MarkupLine($"  [dim]OS:[/] {Markup.Escape(report.EnvironmentHealth.OperatingSystem)} ({report.EnvironmentHealth.Architecture})");
                    AnsiConsole.MarkupLine($"  [dim].NET:[/] {Markup.Escape(report.EnvironmentHealth.DotNetVersion)}");

                    if (report.EnvironmentHealth.DawInstallations.Count > 0)
                    {
                        foreach (var daw in report.EnvironmentHealth.DawInstallations)
                        {
                            AnsiConsole.MarkupLine($"  [green]✓[/] {Markup.Escape(daw.DawName)} ({Markup.Escape(daw.Version)}): [dim]{Markup.Escape(daw.ExecutablePath ?? "")}[/]");
                        }
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("  [yellow]![/] No standard FL Studio installation detected.");
                    }

                    // Verdict
                    AnsiConsole.WriteLine();
                    if (report.IsHealthy)
                    {
                        AnsiConsole.MarkupLine("[green]✓ Repository is healthy and reproducible.[/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]✗ Repository has {report.BlockingIssuesCount} blocking issue(s).[/]");
                    }
                    AnsiConsole.WriteLine();
                }

                // Exit codes (FR-DOC-010)
                if (report.ArtifactHealth.Any(a => a.IsBlocking))
                {
                    Environment.ExitCode = 6;
                }
                else if (report.DependencyHealth.Any(d => d.IsBlocking))
                {
                    Environment.ExitCode = 5;
                }
                else
                {
                    Environment.ExitCode = 0;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.ExitCode = 1;
            }
        }, doctorJsonOption, doctorDirOption);

        // --- dawvc fsck ---
        var fsckCommand = new Command("fsck", "Verify repository object graph and integrity without auto-repair (FR-FSC-001..008, AC-012)");
        var fsckArtifactsOption = new Option<bool>("--artifacts", "Perform deep native adapter validation on project artifacts (AC-012)");
        var fsckJsonOption = new Option<bool>("--json", "Output integrity report as JSON");
        var fsckDirOption = new Option<string?>("--dir", "Repository directory");

        fsckCommand.AddOption(fsckArtifactsOption);
        fsckCommand.AddOption(fsckJsonOption);
        fsckCommand.AddOption(fsckDirOption);

        fsckCommand.SetHandler(async (artifacts, json, dir) =>
        {
            try
            {
                var repoDir = ResolveRepoDirectory(dir);
                var useCase = serviceProvider.GetRequiredService<FsckUseCase>();
                var report = await useCase.ExecuteAsync(new FsckRequest(repoDir, artifacts));

                if (json)
                {
                    var jsonStr = JsonSerializer.Serialize(report, JsonOptions);
                    Console.WriteLine(jsonStr);
                }
                else
                {
                    AnsiConsole.MarkupLine("\n[bold]DAWVC Object Store & Graph Integrity Check (fsck)[/]\n");

                    var repo = report.RepositoryIntegrity;
                    AnsiConsole.MarkupLine($"Objects scanned: [bold]{repo.TotalObjectsScanned}[/] ([green]{repo.ValidObjectsCount} valid[/], [red]{repo.CorruptObjectsCount} corrupt[/], [red]{repo.MissingObjectsCount} missing[/], [yellow]{repo.OrphanObjectsCount} orphan[/])");

                    if (repo.Errors.Count > 0)
                    {
                        AnsiConsole.MarkupLine("\n[bold red]Repository Errors:[/]");
                        foreach (var err in repo.Errors)
                        {
                            AnsiConsole.MarkupLine($"  [red]✗[/] [{err.Code}] {Markup.Escape(err.Target)}: {Markup.Escape(err.Message)}");
                        }
                    }

                    if (repo.Warnings.Count > 0)
                    {
                        AnsiConsole.MarkupLine("\n[bold yellow]Warnings (Orphan objects):[/]");
                        foreach (var w in repo.Warnings)
                        {
                            AnsiConsole.MarkupLine($"  [yellow]![/] [{w.Code}] {Markup.Escape(w.Target)}: {Markup.Escape(w.Message)}");
                        }
                    }

                    if (report.ArtifactIntegrity != null)
                    {
                        var art = report.ArtifactIntegrity;
                        AnsiConsole.MarkupLine($"\nArtifacts validated: [bold]{art.ArtifactsScanned}[/] ([green]{art.ValidArtifactsCount} valid[/], [red]{art.FailedArtifactsCount} failed[/])");

                        if (art.Errors.Count > 0)
                        {
                            AnsiConsole.MarkupLine("\n[bold red]Artifact Validation Failures (AC-012):[/]");
                            foreach (var err in art.Errors)
                            {
                                AnsiConsole.MarkupLine($"  [red]✗[/] [{err.Code}] {Markup.Escape(err.Target)}: {Markup.Escape(err.Message)}");
                            }
                        }
                    }

                    AnsiConsole.WriteLine();
                    if (report.IsHealthy)
                    {
                        AnsiConsole.MarkupLine("[green]✓ Repository and object store integrity verified. Zero corruption detected.[/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[red]✗ Integrity verification failed![/]");
                    }
                    AnsiConsole.WriteLine();
                }

                // Exit codes:
                // 4 if repository corruption
                // 6 if native adapter artifact validation failure
                // 0 if healthy
                if (!report.RepositoryIntegrity.IsIntact)
                {
                    Environment.ExitCode = 4;
                }
                else if (report.ArtifactIntegrity != null && !report.ArtifactIntegrity.IsIntact)
                {
                    Environment.ExitCode = 6;
                }
                else
                {
                    Environment.ExitCode = 0;
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.ExitCode = 1;
            }
        }, fsckArtifactsOption, fsckJsonOption, fsckDirOption);

        rootCommand.AddCommand(initCommand);
        rootCommand.AddCommand(scanCommand);
        rootCommand.AddCommand(commitCommand);
        rootCommand.AddCommand(logCommand);
        rootCommand.AddCommand(checkoutCommand);
        rootCommand.AddCommand(bindCommand);
        rootCommand.AddCommand(statusCommand);
        rootCommand.AddCommand(addCommand);
        rootCommand.AddCommand(branchCommand);
        rootCommand.AddCommand(switchCommand);
        rootCommand.AddCommand(doctorCommand);
        rootCommand.AddCommand(fsckCommand);

        Environment.ExitCode = 0;
        var exitCode = await rootCommand.InvokeAsync(args);
        return exitCode != 0 ? exitCode : Environment.ExitCode;
    }

    private static string ResolveRepoDirectory(string? explicitDir)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir))
        {
            return Path.GetFullPath(explicitDir);
        }

        var found = FileSystemRepositoryContext.FindRoot(Directory.GetCurrentDirectory());
        if (found is null)
        {
            throw new InvalidOperationException("Not a DAWVC repository (or any of the parent directories). Run 'dawvc init' first.");
        }

        return found;
    }
}
