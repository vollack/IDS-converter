namespace IDS4ALL.Core.Import;

/// <summary>Column names and keywords of the input sheet. Same values as the STRING_* constants of custom_functions.py.</summary>
public static class ColumnNames
{
    public const string Entity = "Entity";
    public const string PredefinedType = "PredefinedType";
    public const string Property = "Property";
    public const string PropertySet = "PropertySet";
    public const string PropertyDatatype = "PropertyDatatype";
    public const string PropertyValue = "PropertyValue";
    public const string PropertyUri = "PropertyURI";
    public const string Material = "Material";
    public const string MaterialUri = "MaterialURI";
    public const string Attribute = "Attribute";
    public const string AttributeValue = "AttributeValue";
    public const string Classification = "Classification";
    public const string ClassificationSystem = "ClassificationSystem";
    public const string ClassificationUri = "ClassificationURI";
    public const string PartOfRelation = "PartOfRelation";
    public const string PartOfEntity = "PartOfEntity";
    public const string PartOfPredefinedType = "PartOfPredefinedType";
    public const string Description = "Description";
    public const string SpecificationName = "SpecificationName";
    public const string SpecificationCardinality = "SpecificationCardinality";
    public const string SpecificationIfcVersion = "SpecificationIfcVersion";
    public const string RequirementCardinality = "Cardinality";

    public const string ApplicabilityPrefix = "A.";
    public const string RequirementPrefix = "R.";

    public const string KeywordNone = "_none_";
    public const string KeywordMissing = "_MISSING_";
    public const string DelimiterMergedRows = "\x1f";
}
