namespace AttendanceQR.Api;

/// <summary>
/// Canonicalizes phone numbers so "0501234567", "+994 50 123 45 67" and "994501234567" all match the
/// same account. Stores/compares the last 9 digits (the Azerbaijani subscriber number).
/// </summary>
public static class PhoneNumbers
{
    /// <summary>Digits only, last 9. Null when there aren't enough digits to be a real number.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var digits = new string(input.Where(char.IsDigit).ToArray());
        if (digits.Length < 7) return null;
        return digits.Length > 9 ? digits[^9..] : digits;
    }
}
