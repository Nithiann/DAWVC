using DawVcs.Domain.Dependencies;

namespace DawVcs.Application.Diagnostics;

public sealed record DependencyHealth(
    string Id,
    string Name,
    string Category,
    DependencyRequirement Requirement,
    BindingStatus Status,
    bool IsBlocking,
    string? Locator,
    string? Details);
