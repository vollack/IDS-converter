using static IDS4ALL.Core.Import.ColumnNames;

namespace IDS4ALL.Core.Merging;

/// <summary>The result of <see cref="SpecificationMerger.ComparePrevious"/>. Python returns these variables, the last values of its loop.</summary>
/// <param name="Diff">True if the applicability is different.</param>
/// <param name="DiffGeneral">True if the general data is different.</param>
/// <param name="DiffSpecification">True if the specification data is different.</param>
/// <param name="Requirements">The requirements of the specification that matched, or an empty list.</param>
/// <param name="Previous">The specification that matched. If none matched, the first specification of the list, or null for an empty list.</param>
public sealed record PreviousComparison(
    bool Diff, bool DiffGeneral, bool DiffSpecification, List<FacetData> Requirements, SpecificationDraft? Previous);

/// <summary>Ports of the functions of custom_functions.py that compare and merge specifications.</summary>
public static class SpecificationMerger
{
    private static readonly string[] ValueKeys = [AttributeValue, PropertyValue];

    /// <summary>
    /// Port of compare_previous_generaldata_and_applicability. Looks from the last to the first specification for one with the same
    /// specification data, the same general data (only the keys in <paramref name="separateBy"/>) and the same applicability.
    /// If it finds one, the general data is merged into it and its requirements are returned.
    /// </summary>
    public static PreviousComparison ComparePrevious(
        IReadOnlyList<SpecificationDraft> specs, FacetData general, FacetData specification, List<FacetData> app, bool appSplitted, IReadOnlyList<string> separateBy)
    {
        List<FacetData> requirements = [];
        var diff = true;
        var diffGeneral = true;
        var diffSpecification = true;
        SpecificationDraft? previous = null;

        for (var j = specs.Count - 1; j >= 0; j--)
        {
            previous = specs[j];

            List<string> included = [SpecificationCardinality, SpecificationIfcVersion];
            // With split applicabilities the name is not compared, because a general specification can have another name.
            if (!previous.AppSplitted && !appSplitted) included.Add(SpecificationName);
            diffSpecification = DeepDiffSubset.IncludedPathsDiffer(specification, previous.Spec, included, ignoreOrder: false);
            if (diffSpecification) continue;

            diffGeneral = separateBy.Count > 0 && DeepDiffSubset.IncludedPathsDiffer(general, previous.General, separateBy, ignoreOrder: false);
            if (diffGeneral) continue;

            diff = DeepDiffSubset.ListsDiffer(app, previous.App);
            if (diff) continue;

            requirements = previous.Req;
            previous.General = RequirementMerger.Merge(previous.General, general);
            // Use the name of the specification that was not split: it is the more specific one.
            if (previous.Spec.ContainsKey(SpecificationName) && specification.ContainsKey(SpecificationName) && !appSplitted)
                previous.Spec[SpecificationName][0] = specification[SpecificationName][0];
            break;
        }
        return new PreviousComparison(diff, diffGeneral, diffSpecification, requirements, previous);
    }

    /// <summary>
    /// Port of structure_specifications_by_Ifc_versions. If the two specifications are equal except for the IFC versions,
    /// the requirements of spec2 go into spec1 and spec2 loses the IFC versions of spec1.
    /// </summary>
    public static void StructureByIfcVersions(
        SpecificationDraft spec1, SpecificationDraft spec2, IReadOnlyList<string> spec1Versions, IReadOnlyList<string> spec2Versions, IReadOnlyList<string> separateBy)
    {
        List<string> included = [SpecificationCardinality];
        if (!spec1.AppSplitted && !spec2.AppSplitted) included.Add(SpecificationName);
        if (DeepDiffSubset.IncludedPathsDiffer(spec1.Spec, spec2.Spec, included, ignoreOrder: false)) return;

        var diffGeneral = separateBy.Count > 0 && DeepDiffSubset.IncludedPathsDiffer(spec1.General, spec2.General, separateBy, ignoreOrder: false);
        if (diffGeneral) return;

        if (DeepDiffSubset.ListsDiffer(spec1.App, spec2.App)) return;

        spec1.General = RequirementMerger.Merge(spec1.General, spec2.General);
        foreach (var requirement2 in spec2.Req)
        {
            var found = false;
            foreach (var requirement1 in spec1.Req)
            {
                found = !RequirementMerger.CompareAndMerge(requirement1, requirement2, [Entity, PredefinedType, AttributeValue, PropertyValue], false, true);
                if (found)
                {
                    if (spec1.Spec.ContainsKey(SpecificationName) && spec2.Spec.ContainsKey(SpecificationName) && !spec2.AppSplitted)
                        spec1.Spec[SpecificationName][0] = spec2.Spec[SpecificationName][0];
                    break;
                }
            }

            if (!found)
            {
                var copy = new FacetData();
                foreach (var (key, values) in requirement2) copy[key] = [.. values];
                spec1.Req.Add(copy);
            }
        }

        // spec2 does not need the IFC versions of spec1 any more.
        spec2.Spec[SpecificationIfcVersion] = spec2Versions.Distinct().Where(version => !spec1Versions.Contains(version)).ToList();
    }

    /// <summary>
    /// Port of add_values_to_general_specs. If a specification has a more general applicability than another one with equal
    /// specification data and general data, the requirements of the specific one are merged into the general one.
    /// </summary>
    public static void AddValuesToGeneralSpecs(IReadOnlyList<SpecificationDraft> specs, IReadOnlyList<string> separateBy)
    {
        for (var i = 0; i < specs.Count; i++)
        {
            for (var j = i + 1; j < specs.Count; j++)
            {
                var specI = specs[i];
                var specJ = specs[j];

                if (DeepDiffSubset.IncludedPathsDiffer(specI.Spec, specJ.Spec, [SpecificationCardinality, SpecificationIfcVersion], ignoreOrder: true)) continue;
                if (separateBy.Count > 0 && DeepDiffSubset.IncludedPathsDiffer(specI.General, specJ.General, separateBy, ignoreOrder: true)) continue;

                var appI = specI.App;
                var appJ = specJ.App;
                if (appJ.Count > 0 && appI.Count > 0)
                {
                    if (appI[0].TryGetValue(Entity, out var entityI) && appJ[0].TryGetValue(Entity, out var entityJ) && !entityI.SequenceEqual(entityJ)) continue;
                    if (appI[0].TryGetValue(PredefinedType, out var typeI) && appJ[0].TryGetValue(PredefinedType, out var typeJ) && !typeI.SequenceEqual(typeJ)) continue;
                }
                if (appJ.Count > 1 && appI.Count > 1)
                {
                    if (appI[1].TryGetValue(AttributeValue, out var valueI) && appJ[1].TryGetValue(AttributeValue, out var valueJ) && !valueI.SequenceEqual(valueJ)) continue;
                }

                var diffApp = DeepDiffSubset.CompareApplicability(appI, appJ);

                // The applicability of I is more general: merge the requirements of J into I.
                if (!diffApp.DictionaryItemRemoved && !diffApp.IterableItemRemoved && !diffApp.ValuesChanged)
                {
                    foreach (var requirementI in specI.Req)
                        foreach (var requirementJ in specJ.Req)
                            RequirementMerger.CompareAndMerge(requirementI, requirementJ, ValueKeys, true);
                }

                // The applicability of J is more general: merge the requirements of I into J.
                if (!diffApp.DictionaryItemAdded && !diffApp.IterableItemAdded && !diffApp.ValuesChanged)
                {
                    foreach (var requirementI in specI.Req)
                        foreach (var requirementJ in specJ.Req)
                            RequirementMerger.CompareAndMerge(requirementJ, requirementI, ValueKeys, true);
                }
            }
        }
    }
}
