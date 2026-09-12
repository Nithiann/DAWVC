using Spectre.Console;

namespace DawVcs.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--version") || args.Contains("-v"))
        {
            AnsiConsole.MarkupLine("[bold blue]DAWVC[/] — DAW Version Control [[v0.1.0-preview]]");
            AnsiConsole.MarkupLine("[dim]Paths are locators, never identifiers.[/]\n");
            AnsiConsole.MarkupLine("Gebruik [green]dawvc --help[/] voor een overzicht van alle commando's.");
            return 0;
        }

        if (args.Contains("--help") || args.Contains("-h"))
        {
            AnsiConsole.MarkupLine("[bold blue]DAWVC CLI[/] — Version control and dependency management for DAWs");
            AnsiConsole.MarkupLine("\n[yellow]Beschikbare commando's (MVP v0.1):[/]");
            AnsiConsole.MarkupLine("  [green]init[/]        Initialiseert een nieuwe DAWVC repository");
            AnsiConsole.MarkupLine("  [green]scan[/]        Inspecteert audio- en plugin-dependencies");
            AnsiConsole.MarkupLine("  [green]status[/]      Toont de toestand van de actieve workspace");
            AnsiConsole.MarkupLine("  [green]add[/]         Voegt een dependency of asset toe aan tracking");
            AnsiConsole.MarkupLine("  [green]commit[/]      Maakt een nieuw immutable snapshot");
            AnsiConsole.MarkupLine("  [green]log[/]         Toont de commit-historie");
            AnsiConsole.MarkupLine("  [green]branch[/]      Beheert projectbranches");
            AnsiConsole.MarkupLine("  [green]switch[/]      Schakelt tussen branches");
            AnsiConsole.MarkupLine("  [green]checkout[/]    Herstelt een project-snapshot");
            AnsiConsole.MarkupLine("  [green]doctor[/]      Valideert omgevingsvereisten en ontbrekende plugins");
            AnsiConsole.MarkupLine("  [green]fsck[/]        Controleert de integriteit van de object store");
            return 0;
        }

        AnsiConsole.MarkupLine($"[bold red]Onbekend commando:[/] {args[0]}");
        AnsiConsole.MarkupLine("Voer [green]dawvc --help[/] uit voor geldige opties.");
        return await Task.FromResult(1);
    }
}
