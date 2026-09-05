using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Services;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The face service as it is with no AWS credentials — off. The default double for any controller
/// that takes one and whose test is about something else entirely.
/// </summary>
internal sealed class OffFaceMatch : IFaceMatchService
{
    public bool Enabled => false;

    public Task<FaceMatchOutcome> CompareAsync(byte[] referenceBytes, byte[] checkInBytes, CancellationToken ct = default)
        => Task.FromResult(new FaceMatchOutcome(0, 0, FaceMatchStatus.NotChecked));

    public Task<int> DetectFaceCountAsync(byte[] photoBytes, CancellationToken ct = default)
        => Task.FromResult(-1);
}

/// <summary>The same service switched on — for the one gate that only exists when it is.</summary>
internal sealed class OnFaceMatch : IFaceMatchService
{
    public bool Enabled => true;

    public Task<FaceMatchOutcome> CompareAsync(byte[] referenceBytes, byte[] checkInBytes, CancellationToken ct = default)
        => Task.FromResult(new FaceMatchOutcome(99, 1, FaceMatchStatus.Ok));

    public Task<int> DetectFaceCountAsync(byte[] photoBytes, CancellationToken ct = default)
        => Task.FromResult(1);
}
