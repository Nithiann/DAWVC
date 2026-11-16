namespace DawVcs.Domain.Repositories;

/// <summary>The index may contain unrecoverable staged entries and must not be automatically replaced.</summary>
public sealed class StagingIndexCorruptException(string databasePath, Exception innerException)
    : IOException("The staging index is corrupt. Preserve the database and recover it explicitly before continuing.", innerException)
{
    public string DatabasePath { get; } = databasePath;
}
