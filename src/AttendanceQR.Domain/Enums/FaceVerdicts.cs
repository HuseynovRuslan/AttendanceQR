namespace AttendanceQR.Domain.Enums;

/// <summary>
/// Which <see cref="FaceMatchStatus"/> values are a VERDICT — the comparison ran and said something —
/// as opposed to a state on the way to one.
///
/// The distinction started to matter when a QR-less check-in began deciding the face inside the request
/// (AttendanceController.CompareFaceNowAsync) and the background worker then re-ran the same comparison
/// after the upload. Two writers, one column: a worker that hit an R2 blip or a Rekognition throttle
/// overwrote a Mismatch the employee had just been shown with <c>Error</c>, and the red flag the manager
/// had been promised vanished from the board. A decided verdict is never downgraded by a later failure;
/// the worker stays the retry path for the undecided ones.
/// </summary>
public static class FaceVerdicts
{
    public static bool IsDecided(FaceMatchStatus status) => status is
        FaceMatchStatus.Ok or FaceMatchStatus.Mismatch or FaceMatchStatus.MultiFace or FaceMatchStatus.NoFace;

    /// <summary>A photo the reference must never be seeded from: no face in it, or more than one.</summary>
    public static bool UnfitAsReference(FaceMatchStatus status) => status is
        FaceMatchStatus.NoFace or FaceMatchStatus.MultiFace;
}
