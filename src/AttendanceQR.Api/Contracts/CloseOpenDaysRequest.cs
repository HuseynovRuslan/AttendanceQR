namespace AttendanceQR.Api.Contracts;

/// <summary>
/// Close a batch of days that were checked in and never out, each at its OWN shift end.
///
/// The ids are explicit and never "everything open": the screen lists the days, the admin ticks the
/// ones they are willing to vouch for, and what gets written is what was on screen. A "close all"
/// that reaches days the caller never saw is how somebody is paid for a shift nobody agreed to.
/// </summary>
/// <param name="RecordIds">The open records to close. Anything already closed, out of the caller's
/// scope, or dated today is skipped and counted, never failed — a batch must not be all-or-nothing
/// when the reason one row cannot be closed has nothing to do with the other twenty.</param>
public record CloseOpenDaysRequest(List<Guid> RecordIds);
