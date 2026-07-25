namespace AttendanceQR.Api.Contracts;

/// <summary>
/// Turns the structured name inputs into the canonical <c>FullName</c> plus the trimmed parts to
/// store. When both a first and last name are given, FullName becomes "First Last"; otherwise the
/// supplied FullName is used as-is (the bulk-paste and legacy paths that still send only FullName).
/// One place, so every create/update composes the display name the same way.
/// </summary>
public static class EmployeeName
{
    public static (string FullName, string? First, string? Last) Resolve(string? first, string? last, string fullName)
    {
        var f = string.IsNullOrWhiteSpace(first) ? null : first.Trim();
        var l = string.IsNullOrWhiteSpace(last) ? null : last.Trim();
        var full = f is not null && l is not null ? $"{f} {l}" : fullName.Trim();
        return (full, f, l);
    }
}
