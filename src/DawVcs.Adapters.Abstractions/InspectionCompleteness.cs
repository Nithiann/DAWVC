namespace DawVcs.Adapters.Abstractions;

/// <summary>
/// Status van de volledigheid van projectinspectie (FR-FLP-002, TD §11).
/// </summary>
public enum InspectionCompleteness
{
    Complete = 1,
    Partial = 2,
    HeaderOnly = 3,
    Unknown = 4
}
