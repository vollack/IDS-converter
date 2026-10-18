using System.Runtime.CompilerServices;

namespace IDS4ALL.Core.Merging;

/// <summary>
/// A specification before it becomes an IDS specification: the spec_dict of custom_functions.py.
/// The members are mutable and are shared between specifications in the same places as in Python.
/// </summary>
public sealed class SpecificationDraft
{
    /// <summary>The applicability: one dictionary for each facet.</summary>
    public required List<FacetData> App { get; set; }

    /// <summary>The requirements: one dictionary for each facet.</summary>
    public required List<FacetData> Req { get; set; }

    /// <summary>Phase, Role and Usecase.</summary>
    public required FacetData General { get; set; }

    /// <summary>SpecificationName, SpecificationCardinality and SpecificationIfcVersion.</summary>
    public required FacetData Spec { get; set; }

    /// <summary>True if the applicability was one of several combinations of the OR values of one applicability.</summary>
    public bool AppSplitted { get; set; }

    /// <summary>The separation categories of the specification, for example "_PhaseLP2". Empty if no separation is used.</summary>
    public string SepString { get; set; } = "";

    /// <summary>copy.deepcopy: objects that are shared inside the specification stay shared inside the copy.</summary>
    public SpecificationDraft DeepCopy()
    {
        var copies = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        return new SpecificationDraft
        {
            App = App.Select(item => Copy(item, copies)).ToList(),
            Req = Req.Select(item => Copy(item, copies)).ToList(),
            General = Copy(General, copies),
            Spec = Copy(Spec, copies),
            AppSplitted = AppSplitted,
            SepString = SepString,
        };
    }

    public static FacetData DeepCopy(FacetData data) => Copy(data, new Dictionary<object, object>(ReferenceEqualityComparer.Instance));

    private static FacetData Copy(FacetData data, Dictionary<object, object> copies)
    {
        if (copies.TryGetValue(data, out var known)) return (FacetData)known;
        var copy = new FacetData();
        copies[data] = copy;
        foreach (var (key, values) in data)
        {
            if (!copies.TryGetValue(values, out var knownList))
            {
                knownList = values.ToList();
                copies[values] = knownList;
            }
            copy[key] = (List<string>)knownList;
        }
        return copy;
    }
}
