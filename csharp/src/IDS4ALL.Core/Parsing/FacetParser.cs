using IDS4ALL.Core.Excel;
using IDS4ALL.Core.Import;
using static IDS4ALL.Core.Import.ColumnNames;

namespace IDS4ALL.Core.Parsing;

/// <summary>
/// Ports of the facet parsing functions in custom_functions.py. Dictionaries keep the order of their keys,
/// like Python dictionaries do. The functions that take and return the same dictionary type work in place, like Python.
/// </summary>
public static class FacetParser
{
    /// <summary>Port of pandas_row_to_dict. For general data and specification data. '\&amp;' and '|' have the same meaning.</summary>
    public static OrderedDictionary<string, List<string>> RowToDictionary(IReadOnlyList<Cell> row)
    {
        var result = new OrderedDictionary<string, List<string>>();
        foreach (var (column, values) in row)
        {
            // Cut the characters after the first '.'.
            var dot = column.IndexOf('.');
            var name = dot < 0 ? column : column[..dot];

            var data = string.Join('|', values);
            if (data == KeywordNone || data == KeywordMissing) continue;

            var cleaned = new List<string>();
            foreach (var value in PythonText.Strip(data).Replace("\\&", "|").Split('|'))
                if (value != KeywordMissing && !cleaned.Contains(value))
                    cleaned.Add(value);
            if (cleaned.Count > 0) result[name] = cleaned;
        }
        return result;
    }

    /// <summary>
    /// Port of pandas_row_to_dict_list. One dictionary for each merged row, without the missing values.
    /// A dictionary that is a real subset of another dictionary is removed.
    /// All cells of a row have the same number of values, because the grouping gives the same number to all aggregated columns.
    /// </summary>
    public static List<OrderedDictionary<string, string>> RowToDictionaries(IReadOnlyList<Cell> row)
    {
        var count = row.Max(cell => cell.Values.Count);
        var all = new List<OrderedDictionary<string, string>>();
        for (var i = 0; i < count; i++)
        {
            var dictionary = new OrderedDictionary<string, string>();
            foreach (var (column, values) in row)
                if (values[i] != KeywordMissing)
                    dictionary[column] = values[i];
            all.Add(dictionary);
        }

        return all.Where(small => !all.Any(large => IsRealSubset(small, large))).ToList();
    }

    private static bool IsRealSubset(OrderedDictionary<string, string> small, OrderedDictionary<string, string> large) =>
        small.Count < large.Count && small.All(pair => large.TryGetValue(pair.Key, out var value) && value == pair.Value);

    /// <summary>
    /// Port of split_OR_AND_values. '\&amp;' separates AND values and '|' separates OR values.
    /// The result is key: [[OR value, OR value], [OR value]] with one inner list for each AND value.
    /// The key loses its prefix: "R.Property" becomes "Property".
    /// </summary>
    public static OrderedDictionary<string, List<List<string>>> SplitOrAndValues(OrderedDictionary<string, string> input, bool isEntityBasedApplicability)
    {
        var result = new OrderedDictionary<string, List<List<string>>>();
        var numberOfAndValues = 0;
        foreach (var (fullKey, rawValue) in input)
        {
            var key = fullKey.Split('.')[1];
            var valuesList = new List<List<string>>();
            if (rawValue != KeywordNone && rawValue != KeywordMissing)
            {
                var value = PythonText.Strip(rawValue);
                if (key is PropertyDatatype or PartOfRelation) value = PythonText.Upper(value);

                var andValues = value.Split("\\&");
                // All columns of one facet need the same number of AND values.
                if (numberOfAndValues == 0) numberOfAndValues = andValues.Length;
                if (andValues.Length != numberOfAndValues)
                {
                    // The requirement cardinality is filled in automatically, so it has no AND values. One value applies to all.
                    if (key == RequirementCardinality && andValues.Length == 1)
                        andValues = Enumerable.Repeat(andValues[0], numberOfAndValues).ToArray();
                    else
                        throw new ImportException($"Number of AND values (seperated by \\&) is invalid for {key} {value}. All columns of one IDS facet require the same number of AND values. Required number of AND values: {numberOfAndValues}");
                }

                foreach (var andValue in andValues)
                {
                    if (andValue.Length == 0) continue;
                    var orValues = new List<string>();
                    foreach (var mergedValue in andValue.Split(DelimiterMergedRows))
                    {
                        // A pattern keeps its '|', because '|' already has a meaning in the pattern.
                        if (mergedValue.StartsWith("pattern=", StringComparison.Ordinal)) orValues.Add(mergedValue);
                        else orValues.AddRange(mergedValue.Split('|'));
                    }

                    // Merged rows of an entity-based applicability can include complex restrictions. They are removed.
                    if (isEntityBasedApplicability && orValues.Count > 1)
                        orValues = orValues.Where(orValue => !IsComplexRestriction(orValue)).ToList();

                    var cleaned = orValues.Where(orValue => orValue != KeywordMissing).ToList();
                    if (cleaned.Count > 0) valuesList.Add(cleaned);
                }
            }

            if (valuesList.Count > 0) result[key] = valuesList;
        }
        return result;
    }

    /// <summary>Port of split_AND_values_to_individual_facet_dicts. One dictionary for each AND value.</summary>
    public static List<OrderedDictionary<string, List<string>>> SplitAndValues(OrderedDictionary<string, List<List<string>>> input)
    {
        var facets = new List<OrderedDictionary<string, List<string>>>();
        if (input.Count == 0) return facets;

        var count = input.GetAt(0).Value.Count;
        for (var j = 0; j < count; j++)
        {
            var facet = new OrderedDictionary<string, List<string>>();
            foreach (var (key, andValues) in input)
            {
                // Python stops with an IndexError here. This happens if empty AND values were removed from some columns only.
                if (j >= andValues.Count)
                    throw new ImportException($"The columns of one IDS facet have different numbers of AND values after the empty values were removed. Affected column: {key}");
                facet[key] = andValues[j];
            }
            facets.Add(facet);
        }
        return facets;
    }

    /// <summary>
    /// Port of separate_dict_value. Splits the values of <paramref name="combinedKey"/> at the delimiter.
    /// The first parts stay under the key. The second parts go to <paramref name="secondKey"/>. Entity names become upper case.
    /// </summary>
    public static void SeparateValue(OrderedDictionary<string, List<string>> dictionary, string combinedKey, string secondKey, string delimiter)
    {
        if (!dictionary.TryGetValue(combinedKey, out var orValues)) return;

        var firstParts = new List<string>();
        var secondParts = new List<string>();
        foreach (var orValue in orValues)
        {
            var parts = orValue.Split(delimiter);
            if (combinedKey is Entity or PartOfEntity) parts[0] = PythonText.Upper(parts[0]);
            firstParts.Add(parts[0]);
            // Only a value with exactly one delimiter has a second part.
            if (parts.Length == 2) secondParts.Add(parts[1]);
        }

        if (firstParts.Distinct().Count() > 1 && secondParts.Count > 0)
            throw new ImportException("PredefinedTypes cannot be used in an enumeration in a specification applicability with cardinality \"required\" because separating it into two different facet parameters causes the connection between the PredefinedType and the Entity to be lost. Affected Entity Facet: Entities: "
                + PythonText.Repr(firstParts) + " PredefinedTypes: " + PythonText.Repr(secondParts));
        if (secondParts.Count > 0 && secondParts.Count < orValues.Count)
            throw new ImportException("Values with and without PredefinedType cannot be used in the same enumeration, because the PredefinedType would apply to all values. Affected values: "
                + PythonText.Repr(orValues));

        dictionary[combinedKey] = firstParts;
        if (secondParts.Count > 0) dictionary[secondKey] = secondParts;
    }

    /// <summary>
    /// Port of generate_combinations. A dictionary with several OR values for a key becomes one dictionary for each value.
    /// The result lists all combinations of the variants of all dictionaries.
    /// Like in Python, the combinations share the dictionary instances. A change of one instance shows in all combinations.
    /// </summary>
    public static List<List<OrderedDictionary<string, List<string>>>> GenerateCombinations(IReadOnlyList<OrderedDictionary<string, List<string>>> data)
    {
        var options = data.Select(ItemVariants).ToList();
        return CartesianProduct(options);
    }

    private static List<OrderedDictionary<string, List<string>>> ItemVariants(OrderedDictionary<string, List<string>> item)
    {
        var keyVariants = new List<List<KeyValuePair<string, List<string>>>>();
        foreach (var (key, values) in item)
        {
            keyVariants.Add(values.Count > 1
                ? values.Select(value => new KeyValuePair<string, List<string>>(key, [value])).ToList()
                : [new KeyValuePair<string, List<string>>(key, values)]);
        }

        return CartesianProduct(keyVariants).Select(combination =>
        {
            var variant = new OrderedDictionary<string, List<string>>();
            foreach (var (key, values) in combination) variant[key] = values;
            return variant;
        }).ToList();
    }

    /// <summary>itertools.product: the last list changes fastest. No list gives no combination, an empty list of lists gives one empty combination.</summary>
    private static List<List<T>> CartesianProduct<T>(IReadOnlyList<List<T>> lists)
    {
        var result = new List<List<T>> { new() };
        foreach (var list in lists)
        {
            var next = new List<List<T>>(result.Count * list.Count);
            foreach (var prefix in result)
                foreach (var item in list)
                    next.Add([.. prefix, item]);
            result = next;
        }
        return result;
    }

    /// <summary>Port of is_complex_restriction. A complex restriction starts with a key character.</summary>
    public static bool IsComplexRestriction(string value) =>
        value.StartsWith("pattern=", StringComparison.Ordinal)
        || value.StartsWith("\\<", StringComparison.Ordinal)
        || value.StartsWith("\\>", StringComparison.Ordinal)
        || value.StartsWith("length=", StringComparison.Ordinal)
        || value.StartsWith("length<=", StringComparison.Ordinal)
        || value.StartsWith("length>=", StringComparison.Ordinal);
}
