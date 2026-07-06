namespace AttendanceQR.Domain.Entities;

/// <summary>
/// An admin-declared non-working day (e.g. a holiday the manager decided to grant), on top of the
/// regular weekly WorkDaysMask. A null LocationId applies to every location; a set LocationId
/// applies only to that one.
/// </summary>
public class NonWorkingDay
{
    public NonWorkingDay()
    {
        Id = Guid.NewGuid();
    }

    public Guid Id { get; set; }

    public DateOnly Date { get; set; }

    public string Description { get; set; } = string.Empty;

    public Guid? LocationId { get; set; }
}
