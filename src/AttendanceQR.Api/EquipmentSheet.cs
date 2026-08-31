using ClosedXML.Excel;

namespace AttendanceQR.Api;

/// <summary>One parsed line of the IT equipment register spreadsheet.</summary>
public record EquipmentSheetRow(
    int RowNo,
    string FullName,
    string? Position,
    string? Area,
    string? Equipment,
    string? SystemUnit,
    string? Monitor,
    string? OtherEquipment);

/// <summary>Why a sheet could not be read. <see cref="Ok"/> is the only success.</summary>
public enum EquipmentSheetError
{
    Ok,
    EmptyFile,
    HeaderNotFound,
}

/// <summary>
/// Reads "İT AVADANLIQLARININ SİYAHISI" — the spreadsheet the equipment register is maintained in.
///
/// Pure and stream-based so it can be tested without a database or an HTTP request; the controller
/// only decides what to do with the rows.
/// </summary>
public static class EquipmentSheet
{
    /// <summary>
    /// Column headers as the register writes them, normalised. Columns are located by header TEXT,
    /// never by position — the same rule the employee import follows. Whoever maintains the file adds
    /// and reorders columns, and by position a reordered "Monitor" would land in "Digər avadanlıq"
    /// silently, which is worse than refusing to import.
    /// </summary>
    private static readonly (string Field, string[] Headers)[] Columns =
    [
        ("rowNo", ["sıra №", "sira №", "sıra no", "№", "sıra"]),
        ("fullName", ["soyadı, adı, atasının adı", "soyadi, adi, atasinin adi", "soyadı, adı", "ad soyad", "işçi"]),
        ("position", ["vəzifəsi", "vezifesi", "vəzifə"]),
        ("area", ["işlədiyi ərazi", "isledigi erazi", "ərazi", "ofis"]),
        ("equipment", ["avadanlıq", "avadanliq"]),
        ("systemUnit", ["sistem bloku", "sistem blok"]),
        ("monitor", ["monitor"]),
        ("otherEquipment", ["digər avadanlıq", "diger avadanliq", "digər"]),
    ];

    /// <summary>How many rows from the top to search for the header. The file opens with a title
    /// banner and a blank row, so the header is not row 1.</summary>
    private const int HeaderSearchDepth = 10;

    public static (EquipmentSheetError Error, List<EquipmentSheetRow> Rows) Parse(Stream stream)
    {
        var rows = new List<EquipmentSheetRow>();

        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.FirstOrDefault();
        if (ws is null) return (EquipmentSheetError.EmptyFile, rows);

        var used = ws.RowsUsed().ToList();
        if (used.Count == 0) return (EquipmentSheetError.EmptyFile, rows);

        var map = new Dictionary<string, int>();
        var headerIdx = -1;
        for (var i = 0; i < Math.Min(used.Count, HeaderSearchDepth) && headerIdx < 0; i++)
        {
            var candidate = new Dictionary<string, int>();
            foreach (var cell in used[i].CellsUsed())
            {
                var text = NormalizeHeader(cell.GetString());
                var match = Columns.FirstOrDefault(c => c.Headers.Contains(text));
                if (match.Field is not null && !candidate.ContainsKey(match.Field))
                    candidate[match.Field] = cell.Address.ColumnNumber;
            }

            // Both are required: a row carrying one of them by coincidence (a section caption reading
            // "Avadanlıq") is not the header.
            if (candidate.ContainsKey("fullName") && candidate.ContainsKey("equipment"))
            {
                map = candidate;
                headerIdx = i;
            }
        }

        if (headerIdx < 0) return (EquipmentSheetError.HeaderNotFound, rows);

        // Lines without a "Sıra №" still import — they are numbered after the highest number seen so
        // far, so a row someone added at the bottom without filling the first column is not dropped.
        var fallbackNo = 0;
        foreach (var row in used.Skip(headerIdx + 1))
        {
            var name = Cell(row, map, "fullName");
            if (string.IsNullOrWhiteSpace(name)) continue; // spacer rows between sections

            var rowNo = int.TryParse(Cell(row, map, "rowNo"), out var n) ? n : fallbackNo + 1;
            fallbackNo = Math.Max(fallbackNo, rowNo);

            rows.Add(new EquipmentSheetRow(
                rowNo,
                name.Trim(),
                Trimmed(Cell(row, map, "position")),
                Trimmed(Cell(row, map, "area")),
                Trimmed(Cell(row, map, "equipment")),
                Trimmed(Cell(row, map, "systemUnit")),
                Trimmed(Cell(row, map, "monitor")),
                Trimmed(Cell(row, map, "otherEquipment"))));
        }

        return (EquipmentSheetError.Ok, rows);
    }

    private static string Cell(IXLRow row, Dictionary<string, int> map, string field)
        => map.TryGetValue(field, out var col) ? row.Cell(col).GetString() : string.Empty;

    /// <summary>
    /// Header text reduced to something the literals above can be compared against.
    ///
    /// The dotted capital İ is why this exists: <c>ToLowerInvariant</c> turns «İşlədiyi ərazi» into an
    /// 'i' followed by a COMBINING DOT ABOVE, which is not the plain 'i' anyone types into source. That
    /// column would go unmapped and every row would import with a blank "işlədiyi ərazi" — a silent
    /// hole, not an error.
    /// </summary>
    internal static string NormalizeHeader(string header)
        => header.Trim()
            .Replace('İ', 'i')
            .Replace('I', 'ı')
            .ToLowerInvariant()
            .Replace("\u0307", string.Empty);

    private static string? Trimmed(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
