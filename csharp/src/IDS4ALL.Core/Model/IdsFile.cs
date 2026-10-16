namespace IDS4ALL.Core.Model;

public sealed record IdsFile(
    string Title,
    string? Copyright,
    string? Version,
    string? Description,
    string? Author,
    string? Date,
    string? Purpose,
    string? Milestone,
    IReadOnlyList<Specification> Specifications);
