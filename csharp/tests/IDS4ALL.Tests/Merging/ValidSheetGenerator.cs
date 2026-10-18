using IDS4ALL.Tests.Excel;

namespace IDS4ALL.Tests.Merging;

/// <summary>
/// Generates sheets whose facets are complete, so that most of them lead to IDS files. A facet group is a set of columns that belong
/// to one IDS facet. A row has either all required cells of a group or none, and the optional cells only if the group is present.
/// </summary>
internal static class ValidSheetGenerator
{
    private sealed record Group(string[] Required, string[] Optional);

    private static readonly Group[] ApplicabilityGroups =
    [
        new(["A.Entity"], []),
        new(["A.Property", "A.PropertySet"], ["A.PropertyValue", "A.PropertyDatatype"]),
        new(["A.Material"], []),
        new(["A.Attribute"], ["A.AttributeValue"]),
        new(["A.ClassificationSystem"], ["A.Classification"]),
        new(["A.PartOfEntity"], ["A.PartOfRelation"]),
    ];

    private static readonly Group[] RequirementGroups =
    [
        new(["R.Entity"], ["R.Description.Entity"]),
        new(["R.Property", "R.PropertySet"], ["R.PropertyValue", "R.PropertyDatatype", "R.PropertyURI", "R.Description.Property"]),
        new(["R.Material"], ["R.MaterialURI"]),
        new(["R.Attribute"], ["R.AttributeValue"]),
        new(["R.ClassificationSystem"], ["R.Classification", "R.ClassificationURI"]),
        new(["R.PartOfEntity"], ["R.PartOfRelation"]),
    ];

    private static readonly string[] OptionalColumns =
        ["R.Cardinality", "Phase", "Role", "Usecase", "SpecificationName", "SpecificationCardinality", "SpecificationIfcVersion"];

    /// <summary>Writes the sheet "Data" and returns the columns and the columns that separate the files.</summary>
    public static (List<string> Columns, string[] SeparateBy) Write(Random random, string path)
    {
        // The entity group is always there, so that every specification has an applicability.
        var applicability = new List<Group> { ApplicabilityGroups[0] };
        applicability.AddRange(ApplicabilityGroups.Skip(1).Where(_ => random.Next(4) == 0));
        var requirements = RequirementGroups.Where(_ => random.Next(2) == 0).DefaultIfEmpty(RequirementGroups[random.Next(RequirementGroups.Length)]).ToList();
        var optional = OptionalColumns.Where(_ => random.Next(2) == 0).ToList();

        var groups = applicability.Concat(requirements).ToList();
        var columns = groups.SelectMany(g => g.Required.Concat(g.Optional)).Concat(optional).ToList();

        // A few templates for each group and column, so that rows repeat and merge.
        var templates = groups.ToDictionary(group => group, group => Enumerable.Range(0, random.Next(1, 4)).Select(_ => Template(random, group)).ToList());
        var optionalPools = optional.ToDictionary(column => column, column => Enumerable.Range(0, random.Next(1, 3)).Select(_ => OptionalCell(random, column)).ToList());

        var rows = new List<TestCell?[]> { columns.Select(column => (TestCell?)TestCell.Text(column)).ToArray() };
        for (var r = random.Next(1, 9); r > 0; r--)
        {
            var cells = new Dictionary<string, TestCell?>();
            foreach (var group in groups)
            {
                // The applicability group of the entity is always present. Other groups are sometimes empty.
                var present = group == groups[0] || random.Next(5) != 0;
                var template = templates[group][random.Next(templates[group].Count)];
                foreach (var column in group.Required.Concat(group.Optional))
                    cells[column] = present ? template.GetValueOrDefault(column) : null;
            }
            foreach (var column in optional)
                cells[column] = optionalPools[column][random.Next(optionalPools[column].Count)];
            rows.Add(columns.Select(column => cells[column]).ToArray());
        }

        new XlsxBuilder().Sheet("Data", [.. rows]).Save(path);
        var separateBy = new[] { "Phase", "Role", "Usecase" }.Where(column => columns.Contains(column) && random.Next(2) == 0).ToArray();
        return (columns, separateBy);
    }

    private static Dictionary<string, TestCell?> Template(Random random, Group group)
    {
        var cells = new Dictionary<string, TestCell?>();
        foreach (var column in group.Required) cells[column] = Pick(random, column);
        foreach (var column in group.Optional) cells[column] = random.Next(3) == 0 ? null : Pick(random, column);
        return cells;
    }

    private static TestCell? OptionalCell(Random random, string column) => random.Next(5) == 0 ? null : Pick(random, column);

    private static TestCell Pick(Random random, string column)
    {
        var pool = MergingEquivalenceTests.Pools[column];
        var text = pool[random.Next(pool.Length)];
        if (column == "SpecificationCardinality" && text == "maybe") text = "required";
        return TestCell.Text(text);
    }
}
