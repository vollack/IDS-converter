using IDS4ALL.Core.Excel;
using IDS4ALL.Core.Import;
using IDS4ALL.Core.Merging;
using IDS4ALL.Core.Model;
using static IDS4ALL.Core.Import.ColumnNames;

namespace IDS4ALL.Core.Ids;

/// <summary>The specifications of one IDS file and the general data of the file.</summary>
/// <param name="Specifications">The specifications with the same separation string.</param>
/// <param name="General">The values of Phase, Role and Usecase, each without repeated values.</param>
public sealed record SeparatedSpecifications(List<SpecificationDraft> Specifications, OrderedDictionary<string, List<string>> General);

/// <summary>Ports of separate_specs_by_generaldata and create_ids_specifications.</summary>
public static class SpecificationFactory
{
    private static readonly string[] GeneralKeys = ["Phase", "Role", "Usecase"];

    /// <summary>Port of separate_specs_by_generaldata: groups the specifications by their separation string, one group for each IDS file.</summary>
    public static OrderedDictionary<string, SeparatedSpecifications> Separate(IEnumerable<SpecificationDraft> specifications)
    {
        var result = new OrderedDictionary<string, SeparatedSpecifications>();
        foreach (var spec in specifications)
        {
            if (result.TryGetValue(spec.SepString, out var existing))
            {
                existing.Specifications.Add(spec);
                foreach (var key in GeneralKeys)
                {
                    if (!spec.General.TryGetValue(key, out var values)) continue;
                    foreach (var value in values)
                        if (!existing.General[key].Contains(value)) existing.General[key].Add(value);
                }
            }
            else
            {
                var general = new OrderedDictionary<string, List<string>>();
                foreach (var key in GeneralKeys)
                    general[key] = spec.General.TryGetValue(key, out var values) ? values.Distinct().ToList() : [];
                result[spec.SepString] = new SeparatedSpecifications([spec], general);
            }
        }
        return result;
    }

    /// <summary>Port of create_ids_specifications.</summary>
    /// <exception cref="ImportException">A facet cannot be created.</exception>
    public static List<Specification> Create(IReadOnlyList<SpecificationDraft> drafts, PropertyDescriptions descriptions)
    {
        var result = new List<Specification>();
        var number = 1;
        foreach (var draft in drafts)
        {
            var instructions = string.Join("; ", GeneralKeys
                .Where(draft.General.ContainsKey)
                .Select(key => key + ": " + string.Join(", ", draft.General[key])));

            var minOccurs = 0;
            int? maxOccurs = null;
            if (draft.Spec.TryGetValue(SpecificationCardinality, out var cardinality))
            {
                if (PythonText.Lower(cardinality[0]) == "required") minOccurs = 1;
                if (PythonText.Lower(cardinality[0]) == "prohibited") maxOccurs = 0;
            }

            result.Add(new Specification(
                Name: NameOf(draft, number),
                IfcVersions: draft.Spec[SpecificationIfcVersion],
                MinOccurs: minOccurs,
                MaxOccurs: maxOccurs,
                Instructions: instructions.Length > 0 ? instructions : null,
                Applicability: FacetFactory.Create(draft.App, descriptions),
                Requirements: FacetFactory.Create(draft.Req, descriptions)));
            number++;
        }
        return result;
    }

    /// <summary>The given name, or "Specification 1: IFCWALL.STANDARD IFCSLAB" with the number and the entities of the applicability.</summary>
    private static string NameOf(SpecificationDraft draft, int number)
    {
        if (draft.Spec.TryGetValue(SpecificationName, out var names)) return names[0];

        var title = "Specification " + number;
        if (draft.App.Count > 0 && draft.App[0].TryGetValue(Entity, out var entities))
        {
            title += ":";
            for (var j = 0; j < entities.Count; j++)
            {
                title += " " + entities[j];
                if (draft.App[0].TryGetValue(PredefinedType, out var types)) title += "." + types[j];
            }
        }
        return title;
    }
}
