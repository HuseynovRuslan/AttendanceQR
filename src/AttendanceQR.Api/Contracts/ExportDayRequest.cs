namespace AttendanceQR.Api.Contracts;

/// <summary>
/// The attendance board the admin is looking at, sent for formatting into the workbook the leadership
/// receives every morning. Title is the header line; Date is used for the download filename.
///
/// The rows come from the CLIENT rather than being re-queried here, and deliberately so: telling
/// «Ezamiyyət» from «Məzuniyyət» needs the leave type that only the board carries, and re-deriving a
/// status label server-side would be a second place for that bug to live (it was a real one — a work
/// trip exported as annual leave). <see cref="ExportDayRow.Bucket"/> arrives for the same reason: the
/// counts on the summary sheet are the board's own, so the file and the screen cannot disagree.
/// </summary>
public record ExportDayRequest(
    string Title,
    string Date,
    List<ExportDayRow> Rows,
    /// <summary>Which sites the reader chose, in words — printed on both sheets so the file always
    /// says what it does and does not cover.</summary>
    string? ScopeNote = null,
    /// <summary>
    /// What the board calls each bucket, keyed by bucket name — its own words, sent rather than
    /// repeated here.
    ///
    /// Three of them had already drifted: this sheet said «Gəlib» where the board says «Tamamlayıb»
    /// (a word retired on purpose — «came» read as though somebody still at work had not), and
    /// «Gözləmədə»/«Hazırlanır» where the board says «Gözlənilir»/«Aktivləşdirməyib». A reader
    /// comparing the file with the screen it came from found three columns that named nothing on it.
    /// It also carries the one label only the board can decide: «İşdə» while the day is still
    /// running, «Çıxış yoxdur» once it is over.
    /// </summary>
    Dictionary<string, string>? BucketLabels = null);

public record ExportDayRow(
    string Name,
    string Location,
    string Status,
    string CheckIn,
    string CheckOut,
    string Photo,
    /// <summary>The board's own bucket for this row (countToday): present, absent, incomplete,
    /// onLeave, sick, trip, permission, dayOff, pending, onboarding. Null from an older client, in
    /// which case the person is still counted in the headcount and in no other column.</summary>
    string? Bucket = null,
    string? Position = null);
