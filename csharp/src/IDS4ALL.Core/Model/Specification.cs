namespace IDS4ALL.Core.Model;

/// <param name="MaxOccurs">Null means "unbounded".</param>
public sealed record Specification(
    string Name,
    IReadOnlyList<string> IfcVersions,
    int MinOccurs,
    int? MaxOccurs,
    string? Instructions,
    IReadOnlyList<Facet> Applicability,
    IReadOnlyList<Facet> Requirements);
