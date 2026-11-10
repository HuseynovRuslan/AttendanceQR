namespace AttendanceQR.Domain.Enums;

/// <summary>What became of one presence check. See <see cref="Entities.PresenceCheck"/>.</summary>
public enum PresenceCheckResult
{
    /// <summary>Planned and not yet sent, or sent and waiting for the answer window to run out.</summary>
    Pending = 0,

    /// <summary>Answered from within the branch's radius.</summary>
    Inside = 1,

    /// <summary>Answered from outside the branch's radius — the only result a manager is told about.</summary>
    Outside = 2,

    /// <summary>
    /// The answer window ran out with nothing sent back. Not evidence of anything: a phone in a pocket, a
    /// basement with no signal and a person on a ladder all look the same. A late answer still replaces it.
    /// </summary>
    NoAnswer = 3,

    /// <summary>Never asked: the day closed first, the person went on a field visit, or the checks were switched off.</summary>
    Cancelled = 4,

    /// <summary>The person answered, but the phone gave no position. Not «outside»; they may try again.</summary>
    GpsFailed = 5,
}
