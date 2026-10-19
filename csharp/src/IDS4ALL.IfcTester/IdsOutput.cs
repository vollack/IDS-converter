namespace IDS4ALL.IfcTester;

/// <param name="Xml">The IDS file as text.</param>
/// <param name="IsValid">True when the file is valid against the IDS schema (result of ifctester to_xml).</param>
public sealed record IdsOutput(string Xml, bool IsValid);
