using IDS4ALL.Core.Excel;
using IDS4ALL.Core.Import;
using IDS4ALL.Core.Merging;
using IDS4ALL.Core.Model;
using static IDS4ALL.Core.Import.ColumnNames;

namespace IDS4ALL.Core.Ids;

/// <summary>Port of append_facets: creates the IDS facets from the facet dictionaries.</summary>
public static class FacetFactory
{
    /// <exception cref="ImportException">A facet lacks a required parameter, or a parameter has a value that the IDS facet cannot take.</exception>
    public static List<Facet> Create(IReadOnlyList<FacetData> input, PropertyDescriptions descriptions)
    {
        var facets = new List<Facet>();
        foreach (var data in input)
        {
            var values = Convert(data);
            var (facet, incomplete) = CreateFacet(values, descriptions);
            if (incomplete is not null) throw new ImportException("Incomplete IDS facet: " + Describe(values) + ". " + incomplete);
            if (facet is not null) facets.Add(facet);
        }
        return facets;
    }

    /// <summary>Turns the lists of OR values into simple values and restrictions. A key without values is dropped.</summary>
    private static OrderedDictionary<string, IdsValue> Convert(FacetData data)
    {
        var result = new OrderedDictionary<string, IdsValue>();
        foreach (var (key, values) in data)
        {
            var orValues = values.Distinct().ToList();
            var restrictionBase = "string";
            // The base of a restriction of a property value depends on the data type.
            if (key == PropertyValue && data.TryGetValue(PropertyDatatype, out var datatype))
            {
                if (!DatatypeBases.Values.TryGetValue(datatype[0], out restrictionBase))
                    throw new ImportException("KeyError: " + PythonText.Repr(datatype[0]));
            }

            if (orValues.Count > 1) result[key] = new EnumerationRestriction(orValues, restrictionBase);
            else if (orValues.Count == 1) result[key] = ConvertSingle(orValues[0], restrictionBase);
        }
        return result;
    }

    private static IdsValue ConvertSingle(string text, string restrictionBase)
    {
        if (PythonText.Lower(text) is "true" or "false") text = PythonText.Lower(text);

        if (text.StartsWith("pattern=", StringComparison.Ordinal)) return new PatternRestriction(text[8..], restrictionBase);
        if (text.StartsWith("\\<=", StringComparison.Ordinal)) return Bound(BoundKind.MaxInclusive, text[3..], restrictionBase);
        if (text.StartsWith("\\<", StringComparison.Ordinal)) return Bound(BoundKind.MaxExclusive, text[2..], restrictionBase);
        if (text.StartsWith("\\>=", StringComparison.Ordinal)) return Bound(BoundKind.MinInclusive, text[3..], restrictionBase);
        if (text.StartsWith("\\>", StringComparison.Ordinal)) return Bound(BoundKind.MinExclusive, text[2..], restrictionBase);
        if (text.StartsWith("length=", StringComparison.Ordinal)) return Length(LengthKind.Length, text[7..], restrictionBase);
        if (text.StartsWith("length>=", StringComparison.Ordinal)) return Length(LengthKind.MinLength, text[8..], restrictionBase);
        if (text.StartsWith("length<=", StringComparison.Ordinal)) return Length(LengthKind.MaxLength, text[8..], restrictionBase);
        return new SimpleValue(text);
    }

    private static BoundRestriction Bound(BoundKind kind, string number, string restrictionBase) =>
        new(kind, PythonFloat.Parse(number.Replace(',', '.')), restrictionBase == "string" ? "double" : restrictionBase);

    private static LengthRestriction Length(LengthKind kind, string number, string restrictionBase) =>
        new(kind, PythonFloat.ParseInteger(number.Replace(',', '.')), restrictionBase);

    private static (Facet? Facet, string? Incomplete) CreateFacet(OrderedDictionary<string, IdsValue> values, PropertyDescriptions descriptions)
    {
        IdsValue? Value(string key) => values.GetValueOrDefault(key);

        if (values.ContainsKey(Entity) || values.ContainsKey(PredefinedType))
        {
            if (!values.ContainsKey(Entity)) return (null, "The Entity facet requires the Entity parameter.");
            return (new EntityFacet(values[Entity], Value(PredefinedType), Text(Value(Description), Description)), null);
        }

        if (values.ContainsKey(PropertySet) || values.ContainsKey(Property) || values.ContainsKey(PropertyDatatype) || values.ContainsKey(PropertyValue))
        {
            if (!values.ContainsKey(PropertySet) || !values.ContainsKey(Property))
                return (null, "The Property facet requires the PropertySet and Property parameters.");

            // The description is found by the text of the property set and the property. A restriction never matches.
            var setKey = TextKey(values[PropertySet]);
            var nameKey = TextKey(values[Property]);
            var description = nameKey is null ? null : setKey is null ? descriptions.FindByName(nameKey) : descriptions.Find(setKey, nameKey);
            var dataType = Text(Value(PropertyDatatype), PropertyDatatype)?.Replace(" ", "");
            return (new PropertyFacet(values[PropertySet], values[Property], Value(PropertyValue), dataType,
                Text(Value(PropertyUri), PropertyUri), Text(Value(RequirementCardinality), RequirementCardinality), description), null);
        }

        if (values.ContainsKey(Material))
        {
            return (new MaterialFacet(values[Material], Text(Value(MaterialUri), MaterialUri), Text(Value(RequirementCardinality), RequirementCardinality)), null);
        }

        if (values.ContainsKey(ColumnNames.Attribute) || values.ContainsKey(AttributeValue))
        {
            if (!values.ContainsKey(ColumnNames.Attribute)) return (null, "The Attribute facet requires the Attribute parameter.");
            return (new AttributeFacet(values[ColumnNames.Attribute], Value(AttributeValue), Text(Value(RequirementCardinality), RequirementCardinality)), null);
        }

        if (values.ContainsKey(Classification) || values.ContainsKey(ClassificationSystem) || values.ContainsKey(ClassificationUri))
        {
            if (!values.ContainsKey(ClassificationSystem)) return (null, "The Classification facet requires the Classifiaction system parameter.");
            return (new ClassificationFacet(values[ClassificationSystem], Value(Classification), Text(Value(ClassificationUri), ClassificationUri),
                Text(Value(RequirementCardinality), RequirementCardinality)), null);
        }

        if (values.ContainsKey(PartOfEntity) || values.ContainsKey(PartOfPredefinedType) || values.ContainsKey(PartOfRelation))
        {
            if (!values.ContainsKey(PartOfEntity)) return (null, "The PartOf facet requires the Entity parameter.");
            return (new PartOfFacet(values[PartOfEntity], Value(PartOfPredefinedType), Text(Value(PartOfRelation), PartOfRelation),
                Text(Value(RequirementCardinality), RequirementCardinality)), null);
        }

        return (null, null);
    }

    /// <summary>A parameter that is plain text in an IDS facet. ifctester cannot write a restriction there, Python fails when it writes the file.</summary>
    private static string? Text(IdsValue? value, string parameter) => value switch
    {
        null => null,
        SimpleValue simple => simple.Text,
        _ => throw new ImportException($"The parameter {parameter} cannot have several values or a restriction."),
    };

    /// <summary>str() of a value, to look up a description. A restriction has a text that matches no description: null.</summary>
    private static string? TextKey(IdsValue value) => value is SimpleValue simple ? simple.Text : null;

    /// <summary>The Python repr of the dictionary, for the message. A restriction is shown as an object.</summary>
    private static string Describe(OrderedDictionary<string, IdsValue> values) =>
        "{" + string.Join(", ", values.Select(pair => PythonText.Repr(pair.Key) + ": " +
            (pair.Value is SimpleValue simple ? PythonText.Repr(simple.Text) : "<ifctester.facet.Restriction object>"))) + "}";
}
