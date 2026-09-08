using AttendanceQR.Application.Reporting;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// Whether a site's circle fits the ground it covers — and which of the two wrong answers it is.
///
/// The numbers below are the real ones from 2026-09-08. Qafur Məmmədov Parkı refused 133 scans in
/// forty days against a 150-metre circle, and the CLOSEST of those refusals was from 466 metres: the
/// crew works across a park, and the circle is drawn round one corner of it. Heydər Əliyev Mərkəzi
/// refused 88 with the nearest at exactly 500 against a 500-metre circle — people pressed right up
/// against the line. The first is moved; the second is widened. Telling them apart is the whole job,
/// because widening a circle that is in the wrong PLACE licences scanning from home.
/// </summary>
public class GeofenceFitTests
{
    [Fact]
    public void A_circle_people_are_refused_from_just_outside_is_merely_tight()
    {
        // Heydər Əliyev Mərkəzi: 500-metre circle, nearest refusal at exactly 500.
        Assert.Equal(GeofenceFit.Verdict.Tight, GeofenceFit.Judge(500, rejections: 88, nearestRejectedMeters: 500));
        // Stadion ətrafı: refused from 152 metres against 150. Two metres.
        Assert.Equal(GeofenceFit.Verdict.Tight, GeofenceFit.Judge(150, rejections: 44, nearestRejectedMeters: 152));
    }

    [Fact]
    public void A_circle_whose_nearest_refusal_is_far_out_is_in_the_wrong_place()
    {
        // Qafur Məmmədov Parkı: 150 metres, and nobody was ever refused from closer than 466.
        Assert.Equal(GeofenceFit.Verdict.Misplaced, GeofenceFit.Judge(150, rejections: 133, nearestRejectedMeters: 466));
        // Aeroport yolu -2: a road crew, nearest refusal 926 metres from a 150-metre circle.
        Assert.Equal(GeofenceFit.Verdict.Misplaced, GeofenceFit.Judge(150, rejections: 23, nearestRejectedMeters: 926));
    }

    [Fact]
    public void One_or_two_refusals_are_not_a_verdict()
    {
        // A bad GPS fix, somebody scanning on their way in, one person on the wrong day. The report
        // must not send an admin to move a site over noise.
        Assert.Equal(GeofenceFit.Verdict.Ok, GeofenceFit.Judge(150, rejections: 4, nearestRejectedMeters: 900));
        Assert.Equal(GeofenceFit.Verdict.Ok, GeofenceFit.Judge(150, rejections: 0, nearestRejectedMeters: null));
    }

    [Fact]
    public void A_site_that_refuses_nobody_is_never_flagged()
    {
        Assert.Equal(GeofenceFit.Verdict.Ok, GeofenceFit.Judge(500, rejections: 40, nearestRejectedMeters: null));
    }

    [Fact]
    public void The_boundary_between_the_two_answers_is_one_and_a_half_radii()
    {
        // Inside 1.5×, the person was at the site and the circle was short. Beyond it, they were
        // somewhere else — and that is a different conversation, with a different fix.
        Assert.Equal(GeofenceFit.Verdict.Tight, GeofenceFit.Judge(200, rejections: 10, nearestRejectedMeters: 300));
        Assert.Equal(GeofenceFit.Verdict.Misplaced, GeofenceFit.Judge(200, rejections: 10, nearestRejectedMeters: 301));
    }
}
