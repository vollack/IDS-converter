using System.Xml.Linq;
using IDS4ALL.Core.Model;
using IDS4ALL.IfcTester;

namespace IDS4ALL.Tests;

public sealed class SpecificationDescriptionTests
{
    private static readonly XNamespace Ids = "http://standards.buildingsmart.org/IDS";

    private static IdsFile File(string? description) => new(
        Title: "Description test", Copyright: null, Version: null, Description: null, Author: null, Date: "2026-01-01",
        Purpose: null, Milestone: null,
        Specifications:
        [
            new Specification(
                Name: "Typ 1 – Tür", IfcVersions: ["IFC4"], MinOccurs: 0, MaxOccurs: null, Instructions: null,
                Applicability: [new EntityFacet(new SimpleValue("IFCDOOR"))],
                Requirements: [new AttributeFacet(new SimpleValue("Name"))],
                Description: description),
        ]);

    private static XElement Write(IdsFile file)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ids4all-{Guid.NewGuid():N}.ids");
        try
        {
            Assert.True(new IdsWriter(PythonRuntimeOptions.FromEnvironment()).Write(file, path));
            return XDocument.Load(path).Descendants(Ids + "specification").Single();
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [PythonFact]
    public void Write_WithDescription_WritesTheDescriptionAttribute()
    {
        var specification = Write(File("Bauherr <&> \"ä\""));

        Assert.Equal("Bauherr <&> \"ä\"", specification.Attribute("description")!.Value);
        Assert.Equal("Typ 1 – Tür", specification.Attribute("name")!.Value);
    }

    [PythonFact]
    public void Write_WithoutDescription_WritesNoDescriptionAttribute()
    {
        Assert.Null(Write(File(null)).Attribute("description"));
    }
}
