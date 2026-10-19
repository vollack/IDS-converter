using IDS4ALL.Core.Excel;
using IDS4ALL.Core.Ids;
using IDS4ALL.Core.Import;
using IDS4ALL.Core.Merging;
using IDS4ALL.Core.Model;

namespace IDS4ALL.Core;

/// <param name="Key">The separation string of the file. It is empty when the workbook has no file separators. The Python version puts it into the file name.</param>
/// <param name="File">The content of the IDS file.</param>
public sealed record ConvertedFile(string Key, IdsFile File);

/// <param name="Settings">The entries of the sheet "IDS4ALL".</param>
/// <param name="Files">One entry for each IDS file, in the order of the Python version.</param>
public sealed record Ids4AllConversion(Ids4AllSettings Settings, IReadOnlyList<ConvertedFile> Files);

/// <summary>The whole conversion from an ids4all workbook to IDS records, as IDS4ALL-main.py runs it, without writing files.</summary>
public static class Ids4AllConverter
{
    /// <param name="workbook">The workbook with the sheet "IDS4ALL" and the sheet with the information requirements.</param>
    /// <param name="date">The date of every file, usually today.</param>
    /// <exception cref="ImportException">The sheet is not valid input, or a facet cannot be created.</exception>
    /// <exception cref="XlsxException">The workbook lacks a sheet.</exception>
    public static Ids4AllConversion Convert(XlsxWorkbook workbook, DateOnly date)
    {
        var settings = MetadataReader.Read(workbook);
        var list = SpecificationBuilder.Build(SheetImporter.Import(workbook, settings), settings);
        var files = SpecificationFactory.Separate(list.Specifications)
            .Select(group => new ConvertedFile(group.Key, IdsFileBuilder.Build(group.Value, settings, date, list.Descriptions)))
            .ToList();
        return new Ids4AllConversion(settings, files);
    }
}
