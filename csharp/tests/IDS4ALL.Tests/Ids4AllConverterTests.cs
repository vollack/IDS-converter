using IDS4ALL.Core;
using IDS4ALL.Core.Excel;
using IDS4ALL.Core.Import;
using IDS4ALL.Tests.Excel;

namespace IDS4ALL.Tests;

public sealed class Ids4AllConverterTests
{
    private static readonly DateOnly Date = new(2026, 1, 1);

    public static TheoryData<string> ExampleWorkbooks() => new()
    {
        "IDS4ALL-Template.xlsx",
        "Requirements-openBIM-building-permit-City-of-Vienna-extended.xlsx",
    };

    [Theory]
    [MemberData(nameof(ExampleWorkbooks))]
    public void Convert_ReturnsTheSettingsAndOneFileForEachSeparationKey(string workbook)
    {
        using var opened = XlsxWorkbook.Open(RepositoryRoot.ExcelFile(workbook));

        var conversion = Ids4AllConverter.Convert(opened, Date);

        Assert.Equal(MetadataReader.Read(opened), conversion.Settings);
        Assert.NotEmpty(conversion.Files);
        Assert.All(conversion.Files, file =>
        {
            Assert.Equal(conversion.Settings.Title, file.File.Title);
            Assert.Equal("2026-01-01", file.File.Date);
            Assert.NotEmpty(file.File.Specifications);
        });
        Assert.Equal(conversion.Files.Count, conversion.Files.Select(file => file.Key).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(ExampleWorkbooks))]
    public void Convert_FromAStream_GivesTheSameFilesAsFromThePath(string workbook)
    {
        var path = RepositoryRoot.ExcelFile(workbook);
        using var fromPath = XlsxWorkbook.Open(path);
        using var stream = File.OpenRead(path);
        using var fromStream = XlsxWorkbook.Open(stream);

        var a = Ids4AllConverter.Convert(fromPath, Date);
        var b = Ids4AllConverter.Convert(fromStream, Date);

        Assert.Equal(a.Settings, b.Settings);
        Assert.Equal(a.Files.Select(file => file.Key), b.Files.Select(file => file.Key));
        Assert.Equal(a.Files.Select(file => file.File.Specifications.Count), b.Files.Select(file => file.File.Specifications.Count));
    }

    [Fact]
    public void Convert_WithoutTheMetadataSheet_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ids4all-{Guid.NewGuid():N}.xlsx");
        try
        {
            new XlsxBuilder().Sheet("Data", [TestCell.Text("A.Entity")]).Save(path);
            using var workbook = XlsxWorkbook.Open(path);

            Assert.Throws<XlsxException>(() => Ids4AllConverter.Convert(workbook, Date));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
