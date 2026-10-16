namespace IDS4ALL.Core.Model;

/// <summary>Value of an IDS facet parameter: a simple value or one restriction.</summary>
public abstract record IdsValue;

public sealed record SimpleValue(string Text) : IdsValue;

public sealed record EnumerationRestriction(IReadOnlyList<string> Options, string Base = "string") : IdsValue;

public sealed record PatternRestriction(string Pattern, string Base = "string") : IdsValue;

public enum BoundKind { MinInclusive, MaxInclusive, MinExclusive, MaxExclusive }

public sealed record BoundRestriction(BoundKind Kind, double Bound, string Base = "double") : IdsValue;

public enum LengthKind { Length, MinLength, MaxLength }

public sealed record LengthRestriction(LengthKind Kind, int Length, string Base = "string") : IdsValue;
