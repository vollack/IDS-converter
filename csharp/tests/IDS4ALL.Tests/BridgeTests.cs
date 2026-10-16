using IDS4ALL.Core.Model;
using IDS4ALL.IfcTester;

namespace IDS4ALL.Tests;

public sealed class BridgeTests
{
    private const string Comment = " Created with the IDS4ALL Converter test. ";

    private static SimpleValue S(string text) => new(text);

    private static PropertyFacet Property(string name, string? dataType = null, IdsValue? value = null, string? cardinality = null) =>
        new(S("Pset_WallCommon"), S(name), value, dataType, null, cardinality);

    private static IdsFile BuildFile() => new(
        Title: "Bridge test",
        Copyright: "TU Wien",
        Version: "1.0",
        Description: "Bridge test file",
        Author: "test@example.org",
        Date: "2026-01-01",
        Purpose: "Role: Architect",
        Milestone: "P1, P2",
        Specifications:
        [
            new Specification(
                Name: "Walls",
                IfcVersions: ["IFC4", "IFC4X3_ADD2"],
                MinOccurs: 1,
                MaxOccurs: null,
                Instructions: "Phase: P1, P2; Role: Architect; Usecase: Permit",
                Applicability: [new EntityFacet(S("IFCWALL"), S("STANDARD"))],
                Requirements:
                [
                    new PropertyFacet(S("Pset_WallCommon"), S("FireRating"),
                        new EnumerationRestriction(["REI60", "REI90"]), "IFCLABEL", "https://example.org/p", "required"),
                    Property("ThermalTransmittance", "IFCTHERMALTRANSMITTANCEMEASURE",
                        new BoundRestriction(BoundKind.MaxInclusive, 0.5), "required"),
                    Property("LoadBearingCapacity", "IFCREAL",
                        new BoundRestriction(BoundKind.MinExclusive, 1.5), "optional"),
                    Property("Width", "IFCLENGTHMEASURE", new BoundRestriction(BoundKind.MinInclusive, 0.2)),
                    Property("Height", "IFCLENGTHMEASURE", new BoundRestriction(BoundKind.MaxExclusive, 3)),
                    Property("Reference", "IFCLABEL", new PatternRestriction("^W[0-9]+$"), "optional"),
                    Property("Code", "IFCLABEL", new LengthRestriction(LengthKind.MinLength, 3)),
                    Property("Mark", "IFCLABEL", new LengthRestriction(LengthKind.MaxLength, 8)),
                    Property("Tag", "IFCLABEL", new LengthRestriction(LengthKind.Length, 5)),
                    Property("IsExternal", "IFCBOOLEAN", S("true"), "required"),
                    new AttributeFacet(S("Name"), S("Wall 1"), "required"),
                    new AttributeFacet(S("Description")),
                    new ClassificationFacet(S("Uniclass"), S("21.21"), "https://example.org/c", "required"),
                    new PartOfFacet(S("IFCBUILDINGSTOREY"), null, "IFCRELCONTAINEDINSPATIALSTRUCTURE", "required"),
                    new MaterialFacet(new EnumerationRestriction(["Concrete", "Steel"]), "https://example.org/m", "prohibited"),
                    new EntityFacet(S("IFCWALL"), null, "Wall entity"),
                ]),
            new Specification(
                Name: "Specification 2: IFCSLAB.FLOOR",
                IfcVersions: ["IFC4"],
                MinOccurs: 0,
                MaxOccurs: 0,
                Instructions: null,
                Applicability: [new EntityFacet(S("IFCSLAB"), S("FLOOR"))],
                Requirements: [new AttributeFacet(S("Name"))]),
            new Specification(
                Name: "Specification 3: IFCWALL IFCSLAB",
                IfcVersions: ["IFC2X3"],
                MinOccurs: 0,
                MaxOccurs: null,
                Instructions: "Phase: P1",
                Applicability:
                [
                    new EntityFacet(new EnumerationRestriction(["IFCWALL", "IFCSLAB"])),
                    new PropertyFacet(S("Pset_WallCommon"), S("IsExternal")),
                ],
                Requirements: [new MaterialFacet(S("Concrete"))]),
        ]);

    private static IdsWriter CreateWriter() => new(PythonRuntimeOptions.FromEnvironment());

    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"ids4all-{Guid.NewGuid():N}.ids");

    [PythonFact]
    public void Write_ProducesTheSameFileAsThePythonConverter()
    {
        var path = TempFile();
        try
        {
            var isValid = CreateWriter().Write(BuildFile(), path, Comment);

            Assert.True(isValid);
            var expected = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Golden", "bridge-basic.ids"));
            Assert.Equal(expected, File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [PythonFact]
    public void Write_WithoutComment_LeavesTheIfcTesterOutput()
    {
        var path = TempFile();
        try
        {
            CreateWriter().Write(BuildFile(), path);

            Assert.DoesNotContain("<!--", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [PythonFact]
    public void Write_WithInvalidIfcVersion_ThrowsWithThePythonMessage()
    {
        var file = BuildFile() with
        {
            Specifications = [BuildFile().Specifications[1] with { IfcVersions = ["IFC9"] }],
        };
        var path = TempFile();
        try
        {
            var exception = Assert.Throws<IdsWriteException>(() => CreateWriter().Write(file, path));

            Assert.False(string.IsNullOrWhiteSpace(exception.Message));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FromEnvironment_WithoutVariable_NamesTheVariable()
    {
        var saved = (
            Environment.GetEnvironmentVariable(PythonRuntimeOptions.PythonDllVariable),
            Environment.GetEnvironmentVariable("PYTHONNET_PYDLL"));
        try
        {
            Environment.SetEnvironmentVariable(PythonRuntimeOptions.PythonDllVariable, null);
            Environment.SetEnvironmentVariable("PYTHONNET_PYDLL", null);

            var exception = Assert.Throws<PythonSetupException>(PythonRuntimeOptions.FromEnvironment);

            Assert.Contains(PythonRuntimeOptions.PythonDllVariable, exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable(PythonRuntimeOptions.PythonDllVariable, saved.Item1);
            Environment.SetEnvironmentVariable("PYTHONNET_PYDLL", saved.Item2);
        }
    }
}
