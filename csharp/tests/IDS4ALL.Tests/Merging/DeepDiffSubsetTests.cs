using IDS4ALL.Core.Merging;

namespace IDS4ALL.Tests.Merging;

public sealed class DeepDiffSubsetTests
{
    private static FacetData Facet(params (string Key, string[] Values)[] entries)
    {
        var data = new FacetData();
        foreach (var (key, values) in entries) data[key] = [.. values];
        return data;
    }

    [Fact]
    public void EqualLists_HaveNoChange()
    {
        var a = new[] { Facet(("Entity", ["IFCWALL"])), Facet(("Property", ["Fire", "Load"])) };
        var b = new[] { Facet(("Property", ["Load", "Fire"])), Facet(("Entity", ["IFCWALL"])) };

        Assert.Equal(new ApplicabilityDiff(false, false, false, false, false), DeepDiffSubset.CompareApplicability(a, b));
    }

    [Fact]
    public void ExtraFacet_IsAnAddedIterableItem()
    {
        var a = new[] { Facet(("Entity", ["IFCWALL"])) };
        var b = new[] { Facet(("Entity", ["IFCWALL"])), Facet(("Property", ["Fire"])) };

        Assert.Equal(new ApplicabilityDiff(false, false, true, false, false), DeepDiffSubset.CompareApplicability(a, b));
    }

    [Fact]
    public void SimilarFacets_AreComparedKeyByKey()
    {
        var a = new[] { Facet(("Entity", ["IFCWALL"]), ("PredefinedType", ["SOLIDWALL"]), ("Property", ["Fire", "Load", "Mass"])) };
        var b = new[] { Facet(("Entity", ["IFCWALL"]), ("PredefinedType", ["SOLIDWALL"]), ("Property", ["Fire", "Load", "Mass"]), ("Attribute", ["Name"])) };

        Assert.Equal(new ApplicabilityDiff(true, false, false, false, false), DeepDiffSubset.CompareApplicability(a, b));
    }

    [Fact]
    public void AddedAndRemovedItemAtOnePath_AreOneChangedValue()
    {
        var a = new[] { Facet(("Entity", ["IFCWALL"])) };
        var b = new[] { Facet(("Property", ["Fire"])) };

        Assert.Equal(new ApplicabilityDiff(false, false, false, false, true), DeepDiffSubset.CompareApplicability(a, b));
    }

    [Fact]
    public void IncludedPaths_OnlyLookAtTheGivenKeys()
    {
        var a = Facet(("Phase", ["LP2"]), ("Role", ["Architect"]));
        var b = Facet(("Phase", ["LP2"]), ("Role", ["Engineer"]));

        Assert.False(DeepDiffSubset.IncludedPathsDiffer(a, b, ["Phase"], ignoreOrder: false));
        Assert.True(DeepDiffSubset.IncludedPathsDiffer(a, b, ["Phase", "Role"], ignoreOrder: false));
    }

    [Fact]
    public void IncludedPaths_ListsCompareInOrderUnlessOrderIsIgnored()
    {
        var a = Facet(("Phase", ["LP2", "LP3"]));
        var b = Facet(("Phase", ["LP3", "LP2", "LP3"]));

        Assert.True(DeepDiffSubset.IncludedPathsDiffer(a, b, ["Phase"], ignoreOrder: false));
        Assert.False(DeepDiffSubset.IncludedPathsDiffer(a, b, ["Phase"], ignoreOrder: true));
    }
}
