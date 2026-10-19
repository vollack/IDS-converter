using IDS4ALL.Core.Excel;

namespace IDS4ALL.Tests.Excel;

public sealed class XlsxStreamTests
{
    private const string Sheet = "Data";

    private sealed class TempWorkbook : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ids4all-{Guid.NewGuid():N}.xlsx");

        public void Dispose() => File.Delete(Path);
    }

    [Fact]
    public void Open_Stream_ReadsTheSameRowsAsOpenPath()
    {
        using var file = new TempWorkbook();
        new XlsxBuilder().Sheet(Sheet, [TestCell.Text("a"), TestCell.Text("b")], [TestCell.Number("1"), TestCell.Bool(true)]).Save(file.Path);

        using var fromPath = XlsxWorkbook.Open(file.Path);
        using var stream = File.OpenRead(file.Path);
        using var fromStream = XlsxWorkbook.Open(stream);

        Assert.Equal(fromPath.SheetNames, fromStream.SheetNames);
        Assert.Equal(fromPath.ReadRows(Sheet), fromStream.ReadRows(Sheet));
    }

    [Fact]
    public void Open_Stream_ReadsFromTheCurrentPositionAndLeavesTheStreamOpen()
    {
        using var file = new TempWorkbook();
        new XlsxBuilder().Sheet(Sheet, [TestCell.Text("a")]).Save(file.Path);
        var bytes = File.ReadAllBytes(file.Path);
        using var stream = new MemoryStream();
        stream.Write([1, 2, 3]);
        stream.Write(bytes);
        stream.Position = 3;

        using var workbook = XlsxWorkbook.Open(stream);

        Assert.Equal(["a"], workbook.ReadRows(Sheet)[0]);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void Open_Stream_WithGarbage_ThrowsXlsxException()
    {
        using var stream = new MemoryStream([1, 2, 3, 4]);

        var exception = Assert.Throws<XlsxException>(() => XlsxWorkbook.Open(stream));

        Assert.StartsWith("The stream is not a valid .xlsx workbook", exception.Message);
    }
}
