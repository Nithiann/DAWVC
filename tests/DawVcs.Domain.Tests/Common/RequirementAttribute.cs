using Xunit.Abstractions;
using Xunit.Sdk;

namespace DawVcs.Domain.Tests.Common;

/// <summary>
/// Marks a test method or class with a specific requirement ID for traceability.
/// </summary>
[TraitDiscoverer("DawVcs.Domain.Tests.Common.RequirementDiscoverer", "DawVcs.Domain.Tests")]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public sealed class RequirementAttribute : Attribute, ITraitAttribute
{
    public string RequirementId { get; }

    public RequirementAttribute(string requirementId)
    {
        RequirementId = requirementId;
    }
}

public sealed class RequirementDiscoverer : ITraitDiscoverer
{
    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
    {
        var requirementId = traitAttribute.GetNamedArgument<string>("RequirementId");
        if (string.IsNullOrEmpty(requirementId))
        {
            var constructorArgs = traitAttribute.GetConstructorArguments().ToList();
            if (constructorArgs.Count > 0 && constructorArgs[0] is string id)
            {
                requirementId = id;
            }
        }

        if (!string.IsNullOrEmpty(requirementId))
        {
            yield return new KeyValuePair<string, string>("Requirement", requirementId);
        }
    }
}
