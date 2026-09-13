using System.CommandLine;

using DawVcs.Application.Checkouts;
using DawVcs.Application.Commits;
using DawVcs.Application.Repositories;
using DawVcs.Domain.Repositories;
using DawVcs.Infrastructure.Repositories;

using Microsoft.Extensions.DependencyInjection;

using Spectre.Console;

namespace DawVcs.Cli;

public static class Program
{
    private static readonly string[] MessageAliases = ["-m", "--message"];
    private static readonly string[] LimitAliases = ["-n", "--limit"];

    public static async Task<int> Main(string[] args)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Func<string, IRepositoryContext>>(sp => dir => new FileSystemRepositoryContext(dir));
        services.AddTransient<InitRepositoryUseCase>();
        services.AddTransient<CommitUseCase>();
        services.AddTransient<LogUseCase>();
        services.AddTransient<CheckoutRestoreUseCase>();

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
        commitCommand.AddOption(commitMessageOption);
        commitCommand.AddOption(commitAuthorOption);
        commitCommand.AddOption(commitDirOption);

        commitCommand.SetHandler(async (message, author, dir) =>
        {
            try
            {
                var repoDir = ResolveRepoDirectory(dir);
                var useCase = serviceProvider.GetRequiredService<CommitUseCase>();
                var result = await useCase.ExecuteAsync(new CommitRequest(repoDir, message, author));

                AnsiConsole.MarkupLine($"[green]✓[/] ([yellow]{result.Branch.Value}[/] [bold]{result.CommitId.ToString()[..8]}[/]) {Markup.Escape(result.Message)}");
                AnsiConsole.MarkupLine($"  [dim]Author:[/]   {Markup.Escape(result.Author)}");
                AnsiConsole.MarkupLine($"  [dim]Snapshot:[/] {result.SnapshotId.ToString()[..8]}");
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
        }, commitMessageOption, commitAuthorOption, commitDirOption);

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
        var checkoutCommand = new Command("checkout", "Restore project artifacts from a commit or branch");
        var checkoutRefArg = new Argument<string>("reference", "Commit hash or branch name to restore");
        var checkoutRestoreToOption = new Option<string>("--restore-to", "Target directory to restore project artifacts into") { IsRequired = true };
        var checkoutDirOption = new Option<string?>("--dir", "Repository directory");
        checkoutCommand.AddArgument(checkoutRefArg);
        checkoutCommand.AddOption(checkoutRestoreToOption);
        checkoutCommand.AddOption(checkoutDirOption);

        checkoutCommand.SetHandler(async (reference, restoreTo, dir) =>
        {
            try
            {
                var repoDir = ResolveRepoDirectory(dir);
                var useCase = serviceProvider.GetRequiredService<CheckoutRestoreUseCase>();
                var result = await useCase.ExecuteAsync(new CheckoutRestoreRequest(repoDir, reference, restoreTo));

                AnsiConsole.MarkupLine($"[green]✓[/] Successfully restored snapshot [dim]{result.SnapshotId.ToString()[..8]}[/] to [cyan]{Markup.Escape(result.TargetDirectory)}[/]");
                foreach (var file in result.RestoredFiles)
                {
                    AnsiConsole.MarkupLine($"  [green]+[/] {Markup.Escape(file)}");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Error:[/] {Markup.Escape(ex.Message)}");
                Environment.ExitCode = 1;
            }
        }, checkoutRefArg, checkoutRestoreToOption, checkoutDirOption);

        rootCommand.AddCommand(initCommand);
        rootCommand.AddCommand(commitCommand);
        rootCommand.AddCommand(logCommand);
        rootCommand.AddCommand(checkoutCommand);

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
