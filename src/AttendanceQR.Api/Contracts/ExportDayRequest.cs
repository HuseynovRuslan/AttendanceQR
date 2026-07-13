namespace AttendanceQR.Api.Contracts;

/// <summary>The (already filtered) attendance board the admin sees, sent for formatting into a tidy
/// .xlsx. Title is the header line; Date is used for the download filename.</summary>
public record ExportDayRequest(string Title, string Date, List<ExportDayRow> Rows);

public record ExportDayRow(string Name, string Location, string Status, string CheckIn, string CheckOut, string Photo);
