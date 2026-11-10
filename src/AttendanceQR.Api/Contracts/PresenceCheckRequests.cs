using System.ComponentModel.DataAnnotations;

namespace AttendanceQR.Api.Contracts;

/// <summary>
/// The person's answer to a presence check: the phone's position — or, when the phone would not give one, why
/// (<paramref name="Failure"/>: denied, unavailable, timeout, unsupported).
///
/// The bounds are written as on ScanRequest, for the reasons given there: no [property:] prefix (MVC validates the
/// constructor parameter of a positional record, and throws on metadata found on the property), and double
/// literals (an int bound validates through Convert.ToInt32 and throws on infinity). The handler checks again,
/// because a non-finite coordinate is the one thing that would read as «inside» from anywhere.
/// </summary>
public record PresenceCheckAnswerRequest(
    [Range(-90d, 90d)] double? Latitude,
    [Range(-180d, 180d)] double? Longitude,
    double? AccuracyMeters = null,
    string? Failure = null);

/// <summary>Switch presence checks on for <paramref name="Days"/> days (7, 14 or 30), or off with null or 0.</summary>
public record PresenceCheckSettingRequest(int? Days);
