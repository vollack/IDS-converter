using IDS4ALL.Core.Excel;
using IDS4ALL.Core.Import;
using IDS4ALL.Core.Parsing;
using static IDS4ALL.Core.Import.ColumnNames;

namespace IDS4ALL.Core.Merging;

/// <summary>The descriptions of the properties: (property set, property name) to description. A missing property set is the text "None".</summary>
public sealed class PropertyDescriptions
{
    private readonly OrderedDictionary<(string? PropertySet, string Name), string> _descriptions = [];

    /// <summary>The descriptions in the order in which they were first stored.</summary>
    public IEnumerable<KeyValuePair<(string? PropertySet, string Name), string>> Entries => _descriptions;

    public void Set(string propertySet, string name, string description)
    {
        _descriptions[(propertySet, name)] = description;
        _descriptions[(null, name)] = description;
    }

    /// <summary>The description of the property in the property set, or else the last description of a property with this name.</summary>
    public string? Find(string propertySet, string name) =>
        _descriptions.TryGetValue((propertySet, name), out var exact) ? exact : FindByName(name);

    public string? FindByName(string name) => _descriptions.GetValueOrDefault((null, name));
}

/// <param name="Specifications">The specifications in the order of the input.</param>
/// <param name="Descriptions">The property descriptions that the IDS facets use.</param>
public sealed record SpecificationList(List<SpecificationDraft> Specifications, PropertyDescriptions Descriptions);

/// <summary>Port of the second half of excel_to_spec_list: from the grouped sheet to the list of specifications.</summary>
public static class SpecificationBuilder
{
    private static readonly string[] ValueKeys = [AttributeValue, PropertyValue];
    private static readonly string[] GeneralKeys = ["Phase", "Role", "Usecase"];
    private static readonly string[] IfcVersions = ["IFC2X3", "IFC4", "IFC4X3_ADD2"];

    public static SpecificationList Build(ImportedSheet sheet, Ids4AllSettings settings) =>
        Build(sheet, SheetGrouper.Group(sheet, settings.SeparateBy), settings.SeparateBy, settings.IsEntityBasedApplicability);

    public static SpecificationList Build(ImportedSheet sheet, GroupedSheet grouped, IReadOnlyList<string> separateBy, bool isEntityBasedApplicability)
    {
        var columns = sheet.Columns;
        var descriptions = new PropertyDescriptions();

        IReadOnlyList<string>[] applicabilityColumns =
            [columns.ApplicabilityEntity, columns.ApplicabilityProperty, columns.ApplicabilityMaterial,
             columns.ApplicabilityAttribute, columns.ApplicabilityClassification, columns.ApplicabilityPartOf];
        IReadOnlyList<string>[] requirementColumns =
            [columns.RequirementEntity, columns.RequirementProperty, columns.RequirementMaterial,
             columns.RequirementAttribute, columns.RequirementClassification, columns.RequirementPartOf];
        applicabilityColumns = applicabilityColumns.Where(list => list.Count > 0).ToArray();
        requirementColumns = requirementColumns.Where(list => list.Count > 0).ToArray();

        var specs = new List<SpecificationDraft>();
        for (var i = 0; i < grouped.Rows.Count; i++)
        {
            var general = columns.General.Count > 0 ? FacetParser.RowToDictionary(grouped.Row(i, columns.General)) : new FacetData();
            var specification = columns.Specification.Count > 0 ? FacetParser.RowToDictionary(grouped.Row(i, columns.Specification)) : new FacetData();
            ValidateSpecificationData(specification);

            var app = new List<FacetData>();
            foreach (var facetColumns in applicabilityColumns)
            {
                foreach (var item in FacetParser.RowToDictionaries(grouped.Row(i, facetColumns)))
                    app.AddRange(FacetParser.SplitAndValues(FacetParser.SplitOrAndValues(item, isEntityBasedApplicability)));
            }

            // Combinations of the OR values give separate specifications, so that general requirements can reach specific ones.
            // With the cardinality "required" this would lose the logic.
            var isRequired = specification.TryGetValue(SpecificationCardinality, out var cardinality) && PythonText.Lower(cardinality[0]) == "required";
            var appLists = isRequired ? new List<List<FacetData>> { app } : FacetParser.GenerateCombinations(app);

            foreach (var appList in appLists)
            {
                var appSplitted = appLists.Count > 1;
                foreach (var appDict in appList)
                {
                    FacetParser.SeparateValue(appDict, Entity, PredefinedType, ".");
                    FacetParser.SeparateValue(appDict, PartOfEntity, PartOfPredefinedType, ".");
                }

                var comparison = SpecificationMerger.ComparePrevious(specs, general, specification, appList, appSplitted, separateBy);
                var requirements = comparison.Requirements;

                foreach (var facetColumns in requirementColumns)
                {
                    foreach (var item in FacetParser.RowToDictionaries(grouped.Row(i, facetColumns)))
                        AddRequirements(item, isEntityBasedApplicability, requirements, descriptions);
                }

                if ((comparison.Diff || comparison.DiffGeneral || comparison.DiffSpecification)
                    && (appList.Count > 0 || requirements.Count > 0 || general.Count > 0))
                {
                    specs.Add(new SpecificationDraft
                    {
                        App = appList,
                        Req = requirements,
                        General = SpecificationDraft.DeepCopy(general),
                        Spec = SpecificationDraft.DeepCopy(specification),
                        AppSplitted = appSplitted,
                    });
                }
            }
        }

        if (separateBy.Count > 0) specs = SeparateByGeneralData(specs, separateBy);
        else foreach (var spec in specs) spec.SepString = "";

        // A specification for a subset of the IFC versions of another one with the same applicability gets the requirements of the other one.
        if (sheet.IfcVersionColumnUsed)
        {
            for (var i = 0; i < specs.Count; i++)
            {
                var specI = specs[i];
                if (specI.Spec[SpecificationIfcVersion].Count == 0) continue;
                for (var j = i + 1; j < specs.Count; j++)
                {
                    var versionsI = specI.Spec[SpecificationIfcVersion];
                    if (versionsI.Count == 0) break;
                    var specJ = specs[j];
                    var versionsJ = specJ.Spec[SpecificationIfcVersion];
                    if (versionsJ.Count == 0) continue;

                    if (versionsI.All(versionsJ.Contains))
                        SpecificationMerger.StructureByIfcVersions(specI, specJ, versionsI, versionsJ, separateBy);
                    else if (versionsJ.All(versionsI.Contains))
                        SpecificationMerger.StructureByIfcVersions(specJ, specI, versionsJ, versionsI, separateBy);
                }
            }

            // The restructuring can leave a specification without IFC versions.
            specs = specs.Where(spec => spec.Spec[SpecificationIfcVersion].Count > 0).ToList();
        }

        SpecificationMerger.AddValuesToGeneralSpecs(specs, separateBy);
        return new SpecificationList(specs, descriptions);
    }

    private static void ValidateSpecificationData(FacetData specification)
    {
        if (specification.TryGetValue(SpecificationCardinality, out var cardinality)
            && PythonText.Lower(cardinality[0]) is not ("required" or "prohibited"))
            specification.Remove(SpecificationCardinality);

        if (specification.TryGetValue(SpecificationIfcVersion, out var versions))
        {
            foreach (var version in versions)
                if (!IfcVersions.Contains(PythonText.Upper(version)))
                    throw new ImportException("Invalid IFC version used: " + version);
        }
    }

    private static void AddRequirements(
        OrderedDictionary<string, string> item, bool isEntityBasedApplicability, List<FacetData> requirements, PropertyDescriptions descriptions)
    {
        var requirement = FacetParser.SplitOrAndValues(item, isEntityBasedApplicability);
        // The cardinality column belongs to several facets. A facet with only the cardinality is empty.
        if (requirement.Count == 1 && requirement.ContainsKey(RequirementCardinality)) requirement = new();

        foreach (var arranged in FacetParser.SplitAndValues(requirement))
        {
            foreach (var values in arranged.Values)
            {
                if (values.Count > 1 && values.Any(FacetParser.IsComplexRestriction))
                    throw new ImportException("Complex restrictions (pattern=; \\<=; \\<; \\>=; \\>; length=; length<=; length>=) cannot be part of an enumeration (list of \"or\"-values)");
            }

            // The description of a property belongs to every occurrence of the property in the IDS.
            if (arranged.TryGetValue(Description, out var description) && arranged.TryGetValue(Property, out var property))
            {
                var propertySet = arranged.TryGetValue(PropertySet, out var sets) ? sets[0] : "None";
                descriptions.Set(propertySet, property[0], description[0]);
            }

            FacetParser.SeparateValue(arranged, Entity, PredefinedType, ".");
            FacetParser.SeparateValue(arranged, PartOfEntity, PartOfPredefinedType, ".");

            // The same requirement except for the values: merge the values. Otherwise add the requirement.
            var diffRequirement = true;
            foreach (var existing in requirements)
            {
                diffRequirement = RequirementMerger.CompareAndMerge(existing, arranged, ValueKeys, false);
                if (!diffRequirement) break;
            }
            if (diffRequirement) requirements.Add(arranged);
        }
    }

    /// <summary>
    /// A specification with several values of a separation category becomes one specification for each combination.
    /// Specifications with the same applicability and the same combination merge again.
    /// </summary>
    private static List<SpecificationDraft> SeparateByGeneralData(List<SpecificationDraft> specs, IReadOnlyList<string> separateBy)
    {
        var result = new List<SpecificationDraft>();
        foreach (var spec in specs)
        {
            var options = separateBy.Select(key => spec.General.TryGetValue(key, out var values) ? values : [""]).ToList();
            foreach (var values in CartesianProduct(options))
            {
                var combination = separateBy.Zip(values).ToList();
                var sepString = string.Concat(combination.Where(pair => pair.Second.Length > 0).Select(pair => "_" + pair.First + pair.Second));

                var sameSeparation = result.Where(other => other.SepString == sepString).ToList();
                var comparison = SpecificationMerger.ComparePrevious(sameSeparation, spec.General, spec.Spec, spec.App, spec.AppSplitted, []);
                SpecificationDraft target;
                if (!comparison.Diff)
                {
                    var diffRequirement = true;
                    foreach (var current in spec.Req)
                    {
                        foreach (var previousRequirement in comparison.Requirements)
                        {
                            diffRequirement = RequirementMerger.CompareAndMerge(previousRequirement, current, ValueKeys, false);
                            if (!diffRequirement) break;
                        }
                        if (diffRequirement) comparison.Requirements.Add(SpecificationDraft.DeepCopy(current));
                    }
                    target = comparison.Previous!;
                }
                else
                {
                    target = spec.DeepCopy();
                    target.SepString = sepString;
                    result.Add(target);
                }

                // Each specification keeps only one value of each separation category.
                foreach (var key in GeneralKeys)
                {
                    if (target.General.ContainsKey(key) && separateBy.Contains(key))
                    {
                        var value = combination.First(pair => pair.First == key).Second;
                        if (value.Length > 0) target.General[key] = [value];
                    }
                }
            }
        }
        return result;
    }

    /// <summary>itertools.product: the last list changes fastest.</summary>
    private static List<List<string>> CartesianProduct(List<List<string>> lists)
    {
        var result = new List<List<string>> { new() };
        foreach (var list in lists)
            result = result.SelectMany(prefix => list.Select(item => (List<string>)[.. prefix, item])).ToList();
        return result;
    }
}
