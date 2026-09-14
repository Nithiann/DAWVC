namespace DawVcs.Domain.Dependencies;

/// <summary>
/// Bepaalt hoe een projectafhankelijkheid behandeld wordt bij bundling en restore (FR-DEP-006 t/m FR-DEP-008, TD §21).
/// </summary>
public enum PortabilityMode
{
    /// <summary>
    /// Content wordt byte-exact opgeslagen in de object store en gereconstrueerd bij checkout (eigen samples, recordings).
    /// </summary>
    Bundle,

    /// <summary>
    /// Externe afhankelijkheid die NIET wordt gebundeld (plugin binaries, commerciële software, factory libraries).
    /// </summary>
    ReferenceOnly,

    /// <summary>
    /// Content waarvan de gebruiker expliciet moet beslissen of deze gebundeld mag worden (bijv. samplepacks met onduidelijke licenties).
    /// </summary>
    UserChoice,

    /// <summary>
    /// Expliciet verboden content (systeembestanden, geheime sleutels, machine-specifieke state).
    /// </summary>
    Forbidden,

    /// <summary>
    /// Onbekende herkomst; vereist nadere classificatie door gebruiker of adapter.
    /// </summary>
    Unknown
}
