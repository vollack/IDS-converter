using System.Globalization;
using IDS4ALL.Core.Import;
using IDS4ALL.Core.Merging;
using IDS4ALL.Core.Model;

namespace IDS4ALL.Core.Ids;

/// <summary>Port of the construction of one IDS file in create_ids_files of IDS4ALL-main.py, without the output.</summary>
public static class IdsFileBuilder
{
    /// <param name="group">The specifications of one file, see <see cref="SpecificationFactory.Separate"/>.</param>
    /// <param name="settings">The metadata of the workbook.</param>
    /// <param name="date">The date of the file, usually today.</param>
    /// <exception cref="ImportException">A facet cannot be created.</exception>
    public static IdsFile Build(SeparatedSpecifications group, Ids4AllSettings settings, DateOnly date, PropertyDescriptions descriptions)
    {
        // The phases are the milestone. Roles and use cases are the purpose.
        var milestone = string.Join(", ", group.General["Phase"]);
        List<string> purposeParts = [];
        if (group.General["Role"].Count > 0) purposeParts.Add("Role: " + string.Join(", ", group.General["Role"]));
        if (group.General["Usecase"].Count > 0) purposeParts.Add("Usecase: " + string.Join(", ", group.General["Usecase"]));
        var purpose = string.Join("; ", purposeParts);

        return new IdsFile(
            Title: settings.Title,
            Copyright: settings.Copyright,
            Version: settings.Version,
            Description: settings.Description,
            Author: settings.Author,
            Date: date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Purpose: purpose.Length > 0 ? purpose : null,
            Milestone: milestone.Length > 0 ? milestone : null,
            Specifications: SpecificationFactory.Create(group.Specifications, descriptions));
    }
}
