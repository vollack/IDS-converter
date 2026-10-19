namespace IDS4ALL.Core.Model;

/// <param name="MaxOccurs">Null means "unbounded".</param>
/// <param name="Description">The description attribute of the specification. The Excel import never sets it. A host that completes the records may.</param>
public sealed record Specification(
    string Name,
    IReadOnlyList<string> IfcVersions,
    int MinOccurs,
    int? MaxOccurs,
    string? Instructions,
    IReadOnlyList<Facet> Applicability,
    IReadOnlyList<Facet> Requirements,
    string? Description = null);
