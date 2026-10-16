namespace IDS4ALL.Core.Model;

/// <summary>
/// An IDS facet. A null parameter is passed on as None. ifctester then writes the XSD default,
/// for example cardinality="required".
/// </summary>
public abstract record Facet;

public sealed record EntityFacet(IdsValue Name, IdsValue? PredefinedType = null, string? Instructions = null) : Facet;

public sealed record PropertyFacet(
    IdsValue PropertySet,
    IdsValue BaseName,
    IdsValue? Value = null,
    string? DataType = null,
    string? Uri = null,
    string? Cardinality = null,
    string? Instructions = null) : Facet;

public sealed record AttributeFacet(IdsValue Name, IdsValue? Value = null, string? Cardinality = null) : Facet;

public sealed record ClassificationFacet(
    IdsValue System,
    IdsValue? Value = null,
    string? Uri = null,
    string? Cardinality = null) : Facet;

public sealed record PartOfFacet(
    IdsValue Name,
    IdsValue? PredefinedType = null,
    string? Relation = null,
    string? Cardinality = null) : Facet;

public sealed record MaterialFacet(IdsValue? Value = null, string? Uri = null, string? Cardinality = null) : Facet;
