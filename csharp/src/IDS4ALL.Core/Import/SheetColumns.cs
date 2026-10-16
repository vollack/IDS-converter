using static IDS4ALL.Core.Import.ColumnNames;

namespace IDS4ALL.Core.Import;

/// <summary>The columns of the input sheet that belong to each facet. Port of the start of excel_to_spec_list.</summary>
public sealed record SheetColumns(
    IReadOnlyList<string> ApplicabilityEntity,
    IReadOnlyList<string> ApplicabilityProperty,
    IReadOnlyList<string> ApplicabilityMaterial,
    IReadOnlyList<string> ApplicabilityAttribute,
    IReadOnlyList<string> ApplicabilityClassification,
    IReadOnlyList<string> ApplicabilityPartOf,
    IReadOnlyList<string> RequirementEntity,
    IReadOnlyList<string> RequirementProperty,
    IReadOnlyList<string> RequirementMaterial,
    IReadOnlyList<string> RequirementAttribute,
    IReadOnlyList<string> RequirementClassification,
    IReadOnlyList<string> RequirementPartOf,
    IReadOnlyList<string> General,
    IReadOnlyList<string> Specification)
{
    private static readonly string[] EntityBasedRemoved =
    [
        ApplicabilityPrefix + Property, ApplicabilityPrefix + PropertySet, ApplicabilityPrefix + PropertyValue,
        ApplicabilityPrefix + PropertyDatatype, ApplicabilityPrefix + Material, ApplicabilityPrefix + Classification,
        ApplicabilityPrefix + ClassificationSystem, ApplicabilityPrefix + PartOfEntity, ApplicabilityPrefix + PartOfRelation,
    ];

    /// <summary>All known columns, each once, in the order of the facet lists.</summary>
    public IReadOnlyList<string> Relevant =>
        new[]
        {
            ApplicabilityEntity, ApplicabilityProperty, ApplicabilityMaterial, ApplicabilityAttribute,
            ApplicabilityClassification, ApplicabilityPartOf, RequirementEntity, RequirementProperty,
            RequirementMaterial, RequirementAttribute, RequirementClassification, RequirementPartOf, General, Specification,
        }.SelectMany(list => list).Distinct().ToList();

    /// <exception cref="ImportException">A column name occurs twice, or no known column exists.</exception>
    public static SheetColumns From(IReadOnlyList<string> allColumns, bool isEntityBasedApplicability, string sheetName)
    {
        // pandas names a repeated column "name.1". Python checks for that suffix.
        var duplicates = allColumns.Where(name => name.EndsWith(".1", StringComparison.Ordinal))
            .Select(name => name[..^2]).Distinct().ToList();
        if (duplicates.Count > 0)
            throw new ImportException("Column names must be unique. The following column names occur multiple times: " + string.Join(", ", duplicates));

        var columns = isEntityBasedApplicability
            ? allColumns.Where(name => !EntityBasedRemoved.Contains(name)).ToList()
            : allColumns.ToList();

        IReadOnlyList<string> Existing(params string[] names) => names.Where(columns.Contains).ToList();

        const string a = ApplicabilityPrefix;
        const string r = RequirementPrefix;
        var result = new SheetColumns(
            ApplicabilityEntity: Existing(a + Entity),
            ApplicabilityProperty: Existing(a + Property, a + PropertySet, a + PropertyValue, a + PropertyDatatype),
            ApplicabilityMaterial: Existing(a + Material),
            ApplicabilityAttribute: Existing(a + ColumnNames.Attribute, a + AttributeValue),
            ApplicabilityClassification: Existing(a + Classification, a + ClassificationSystem),
            ApplicabilityPartOf: Existing(a + PartOfEntity, a + PartOfRelation),
            RequirementEntity: Existing(r + Entity, r + Description + ".Entity"),
            RequirementProperty: Existing(r + Property, r + PropertySet, r + PropertyValue, r + PropertyDatatype,
                r + PropertyUri, r + RequirementCardinality, r + Description + ".Property"),
            RequirementMaterial: Existing(r + Material, r + MaterialUri, r + RequirementCardinality),
            RequirementAttribute: Existing(r + ColumnNames.Attribute, r + AttributeValue, r + RequirementCardinality),
            RequirementClassification: Existing(r + Classification, r + ClassificationSystem, r + ClassificationUri, r + RequirementCardinality),
            RequirementPartOf: Existing(r + PartOfEntity, r + PartOfRelation, r + RequirementCardinality),
            General: Existing("Phase", "Role", "Usecase"),
            Specification: Existing(SpecificationCardinality, SpecificationIfcVersion, SpecificationName));

        if (result.Relevant.Count == 0)
            throw new ImportException($"No known columns found in sheet \"{sheetName}\". Check the sheet name, the column names, and the number of skipped rows.");
        return result;
    }
}
