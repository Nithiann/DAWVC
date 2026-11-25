using System.Runtime.CompilerServices;

// Incoming transport sessions share the Application ownership domain without a public reentrancy API.
[assembly: InternalsVisibleTo("DawVcs.Infrastructure")]
