namespace AttendanceQR.Api.Contracts;

/// <summary>
/// PUT /api/notifications/preferences — one category's settings as the page saves them, whole. Lists, not flags, so a
/// new type needs no contract change. Validated by hand in the controller (an attribute on a positional record is the
/// trap that once failed every scan).
/// </summary>
/// <param name="EnabledTypes">Type keys switched on (NotificationTypes). Empty = everything off.</param>
/// <param name="BranchMode">«all» (every branch the person may see) or «selected».</param>
/// <param name="LocationIds">The picked branches, for «selected». Must all be inside the person's scope.</param>
public record NotificationPreferencesRequest(
    string? Category,
    IReadOnlyList<string>? EnabledTypes,
    string? BranchMode,
    IReadOnlyList<Guid>? LocationIds,
    bool InApp,
    bool Push,
    bool Web);
