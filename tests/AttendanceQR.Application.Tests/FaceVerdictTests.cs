using AttendanceQR.Domain.Enums;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The rule that keeps two writers of one column honest. A QR-less check-in decides the face inside the
/// request and shows the person the verdict; the background worker re-runs after the upload. Without
/// this rule a worker-side R2 blip turned a Mismatch the employee had just been told about into an
/// unflagged Error, and the manager's red flag — the whole anchor at a branch with no poster — vanished.
/// </summary>
public class FaceVerdictTests
{
    [Theory]
    [InlineData(FaceMatchStatus.Ok)]
    [InlineData(FaceMatchStatus.Mismatch)]
    [InlineData(FaceMatchStatus.MultiFace)]
    [InlineData(FaceMatchStatus.NoFace)]
    public void A_verdict_is_decided_and_never_downgraded(FaceMatchStatus status)
    {
        Assert.True(FaceVerdicts.IsDecided(status));
    }

    [Theory]
    [InlineData(FaceMatchStatus.NotChecked)]
    [InlineData(FaceMatchStatus.NoReference)]
    [InlineData(FaceMatchStatus.Error)]
    public void A_state_on_the_way_to_a_verdict_is_still_the_workers_to_decide(FaceMatchStatus status)
    {
        // Error included on purpose: it is the retry case, not an answer.
        Assert.False(FaceVerdicts.IsDecided(status));
    }

    [Fact]
    public void Only_a_faceless_or_crowded_first_selfie_is_refused_as_the_reference()
    {
        // Seventeen of the nineteen QR-less people have no reference; the first selfie becomes it.
        // A ceiling shot promoted to reference would make every later morning NoFace with the fault
        // pinned on the wrong photo.
        Assert.True(FaceVerdicts.UnfitAsReference(FaceMatchStatus.NoFace));
        Assert.True(FaceVerdicts.UnfitAsReference(FaceMatchStatus.MultiFace));

        // One clear face — even one the reference has nothing yet to compare with — seeds it.
        Assert.False(FaceVerdicts.UnfitAsReference(FaceMatchStatus.NoReference));
        Assert.False(FaceVerdicts.UnfitAsReference(FaceMatchStatus.Ok));
        Assert.False(FaceVerdicts.UnfitAsReference(FaceMatchStatus.NotChecked));
    }
}
