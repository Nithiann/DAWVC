using System.Diagnostics;

using DawVcs.Application.Checkouts;
using DawVcs.Application.Operations;
using DawVcs.Application.Recovery;
using DawVcs.Application.Remotes;
using DawVcs.Domain.Common;
using DawVcs.Domain.Repositories;
using DawVcs.Infrastructure.Remotes;
using DawVcs.Infrastructure.Repositories;
using DawVcs.Infrastructure.Storage;

namespace DawVcs.RecoveryCrashHost;

/// <summary>Test-only process host: deliberately kill without managed disposal at publication checkpoints.</summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var root = args[1];
        if (args[0] is "hold-operation" or "hold-creation")
        {
            using var operation = RepositoryOperation.Enter(root, create: args[0] == "hold-creation", bareCreation: args[0] == "hold-creation");
            Console.WriteLine("operation-held");
            await Console.In.ReadLineAsync();
            return 0;
        }
        var desired = args[2];
        var hits = 0;
        void Checkpoint(string point)
        {
            if (point != desired.Split(':')[0]) return;
            hits++;
            var occurrence = desired.Contains(':', StringComparison.Ordinal) ? int.Parse(desired.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture) : 1;
            if (hits != occurrence) return;
            using (var marker = new FileStream(Path.Combine(root, ".dawvc", "killed-at"), FileMode.Create, FileAccess.Write, FileShare.None))
            { marker.Write(System.Text.Encoding.UTF8.GetBytes(desired)); marker.Flush(flushToDisk: true); }
            Process.GetCurrentProcess().Kill();
            Thread.Sleep(Timeout.Infinite);
        }
        static IRepositoryContext Context(string path) => new FileSystemRepositoryContext(path);
        if (args[0] == "recover")
        {
            await new WorkspaceRecoveryUseCase(Context, Checkpoint).RollbackAsync(root);
        }
        else
        {
            var checkout = new CheckoutUseCase(Context, null, Checkpoint);
            if (args[0] == "pull")
            {
                var fetch = new FetchUseCase(Context, new DefaultRemoteTransportFactory(), path => new QuarantineObjectStore(path));
                await new PullUseCase(Context, fetch, checkout).ExecuteAsync(new PullRequest(root));
            }
            else if (args[0] == "switch") await checkout.ExecuteAsync(new CheckoutRequest(root, "incoming", Force: true));
            else
            {
                using var context = Context(root);
                var old = context.GetBranchCommit(BranchName.Main);
                var next = context.GetBranchCommit(BranchName.Parse("incoming"))!.Value;
                await checkout.ExecuteFastForwardAsync(new FastForwardTarget(root, BranchName.Main, old, next), true);
            }
        }
        return 0; // Test fails if the requested checkpoint was never reached.
    }
}
