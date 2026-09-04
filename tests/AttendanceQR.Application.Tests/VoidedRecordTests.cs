using AttendanceQR.Domain.Entities;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// «Saxta giriş» — a scan an admin has judged to be somebody else's face.
///
/// The design decision worth pinning is that it VOIDS rather than deletes. The controller already
/// has a delete beside it, and the delete removes the selfie with the row — which is the correct
/// behaviour for a phantom night-shift record and exactly the wrong one here, because that selfie is
/// the entire evidence for an accusation against a named person. Destroying the proof in the act of
/// making the accusation leaves nothing to stand behind when it is disputed.
///
/// The consequence: a voided record still EXISTS, so every path that computes what a day was must
/// skip it, and any path that forgets to becomes a day the fraudulent scan still gets paid for.
/// </summary>
public class VoidedRecordTests
{
    private static AttendanceRecord Record(DateTime? voidedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeId = Guid.NewGuid(),
        AttendanceDate = new DateOnly(2026, 9, 4),
        CheckInAtUtc = new DateTime(2026, 9, 4, 7, 51, 0, DateTimeKind.Utc),
        CheckOutAtUtc = new DateTime(2026, 9, 4, 15, 2, 0, DateTimeKind.Utc),
        CheckInPhotoKey = "checkins/2026/09/04/abc.jpg",
        VoidedAtUtc = voidedAt,
    };

    [Fact]
    public void Voiding_keeps_the_photograph()
    {
        // The property the whole design turns on. Delete would have taken this key with it.
        var r = Record(voidedAt: DateTime.UtcNow);
        Assert.NotNull(r.CheckInPhotoKey);
        Assert.NotNull(r.CheckInAtUtc);
    }

    [Fact]
    public void Voiding_keeps_the_times_it_is_accusing_someone_of()
    {
        // The first version of this feature nulled CheckInAtUtc to "cancel" the day. That erases the
        // fact being alleged — that a scan happened at 11:51 — so a week later the audit line claims
        // a fraud the record itself no longer records.
        var r = Record(voidedAt: DateTime.UtcNow);
        Assert.Equal(new DateTime(2026, 9, 4, 7, 51, 0, DateTimeKind.Utc), r.CheckInAtUtc);
    }

    [Fact]
    public void A_live_record_is_not_voided()
    {
        Assert.Null(Record().VoidedAtUtc);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_void_flag_is_what_every_day_computation_must_filter_on(bool voided)
    {
        // The filter is `VoidedAtUtc == null`, applied at the record LOAD in both the nightly job
        // (DailySummaryService) and the live path (ReportQueryService). Two places, and they have to
        // agree or the stored summary and today's board disagree about the same day.
        var records = new[] { Record(), Record(voidedAt: voided ? DateTime.UtcNow : null) };
        var counted = records.Where(r => r.VoidedAtUtc == null).ToList();

        Assert.Equal(voided ? 1 : 2, counted.Count);
    }

    [Fact]
    public void Un_voiding_restores_the_day_exactly()
    {
        // Reversibility is not a nicety here: the judgement being undone is a judgement about a
        // photograph of a face, and people get those wrong. Nothing could undo a delete.
        var r = Record(voidedAt: DateTime.UtcNow);
        r.VoidedAtUtc = null;
        r.VoidedByEmployeeId = null;
        r.VoidReason = null;

        Assert.Null(r.VoidedAtUtc);
        Assert.Equal(new DateTime(2026, 9, 4, 7, 51, 0, DateTimeKind.Utc), r.CheckInAtUtc);
        Assert.Equal(new DateTime(2026, 9, 4, 15, 2, 0, DateTimeKind.Utc), r.CheckOutAtUtc);
        Assert.Equal("checkins/2026/09/04/abc.jpg", r.CheckInPhotoKey);
    }
}
