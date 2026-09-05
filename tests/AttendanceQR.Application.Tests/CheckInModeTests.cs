using AttendanceQR.Application.Common;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Who has a poster, and whose radius refuses — resolved in ONE place.
///
/// The branch answers for everybody posted to it, which is the default and should stay the default:
/// the nineteen people who spent five weeks invisible were not missing a per-person permission, they
/// had one. What was missing was a fact about the PLACE. So a per-person value is an EXCEPTION — the
/// driver whose branch has a poster he is never near, the one person moved onto a poster-less patch
/// before the whole branch is — and null, the state every employee starts in, means "ask the branch".
///
/// It is one function because three callers ask the same question: the scan endpoint that accepts or
/// refuses, the profile the phone reads before it decides whether to open a camera, and the admin
/// screens. The moment they answer it separately, somebody gets a screen offering a selfie and a
/// server demanding a poster.
/// </summary>
public class CheckInModeTests
{
    [Fact]
    public void With_no_exception_the_branch_decides()
    {
        Assert.True(CheckInMode.IsQrless(branchQrless: true, employeeOverride: null));
        Assert.False(CheckInMode.IsQrless(branchQrless: false, employeeOverride: null));
        Assert.True(CheckInMode.IsFenced(branchRequiresFence: true, employeeOverride: null));
        Assert.False(CheckInMode.IsFenced(branchRequiresFence: false, employeeOverride: null));
    }

    [Fact]
    public void One_person_can_be_poster_less_at_a_branch_that_has_a_poster()
    {
        // The driver whose branch has a code on the wall he is never standing next to.
        Assert.True(CheckInMode.IsQrless(branchQrless: false, employeeOverride: true));
    }

    [Fact]
    public void One_person_can_be_held_to_the_poster_at_a_branch_that_has_none()
    {
        // The other direction has to work too, or the override is a switch that only turns on: a
        // branch going poster-less with one office worker who still walks past the code.
        Assert.False(CheckInMode.IsQrless(branchQrless: true, employeeOverride: false));
    }

    [Fact]
    public void One_persons_scans_can_be_measured_while_the_branch_stays_fenced()
    {
        Assert.False(CheckInMode.IsFenced(branchRequiresFence: true, employeeOverride: false));
    }

    [Fact]
    public void And_one_person_can_stay_fenced_while_the_branch_is_being_measured()
    {
        Assert.True(CheckInMode.IsFenced(branchRequiresFence: false, employeeOverride: true));
    }

    [Fact]
    public void False_is_a_pinned_exception_and_must_never_be_confused_with_absent()
    {
        // The trap this whole tri-state exists to avoid: a form that sends `false` for "leave it
        // alone" would drag a person off a branch setting that was deliberately switched on. Null
        // and false are different answers and the resolver must treat them so.
        Assert.NotEqual(
            CheckInMode.IsQrless(branchQrless: true, employeeOverride: null),
            CheckInMode.IsQrless(branchQrless: true, employeeOverride: false));
    }
}
