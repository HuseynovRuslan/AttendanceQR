using System.Text.Json;
using AttendanceQR.Api.Approvals;
using AttendanceQR.Domain.Entities;
using AttendanceQR.Domain.Enums;
using AttendanceQR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static AttendanceQR.Application.Tests.ApprovalWorld;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// «Yeni işçi» builds two values out of what was typed, and each part can fit while the whole does not: «first last»
/// goes into Employees.FullName, and «email:» + the address into ChangeRequests.DedupeKey. Unchecked, the long e-mail
/// was a 500 at «Göndər», and the long name went in and then failed at every approval as «ApplyFailed», for good. Both
/// limits are read from the model, as the columns are.
/// </summary>
public class ApprovalTextLimitTests
{
    private static EmployeeCreateRequestBody NoPhone(Guid branch, string email) =>
        new("Aysel", "Məmmədova", null, null, email, null, null, branch, null, null);

    [Fact]
    public async Task A_first_and_last_name_that_fit_alone_but_not_together_are_refused_at_the_form()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        using var db = a.Db();
        var max = FieldLimits.MaxLength<Employee>(db, nameof(Employee.FullName))!.Value;

        // Each part is within its own 100; «first last» is one more than the column.
        var first = new string('A', 100);
        var last = new string('B', max - first.Length);
        var refused = await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1, first: first, last: last), null, default);
        Assert.Equal(400, refused.Status);
        Assert.Equal("FullNameTooLong", Json(refused.Body).GetProperty("error").GetString());
        Assert.Empty(db.ChangeRequests.AsNoTracking());

        // One shorter and it fits: the request goes in and the approval writes the person.
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1, first: first, last: last[1..]), null, default));
        Assert.Equal(200, (await a.Desk().ApproveAsync(a.Operator1, id, null, default)).Status);
        Assert.Equal(max, Assert.Single(a.PeopleWithPhone("551234567")).FullName.Length);
    }

    [Fact]
    public async Task An_email_whose_dedupe_key_would_not_fit_is_refused_and_one_that_fits_goes_in()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        using var db = a.Db();
        var max = FieldLimits.MaxLength<ChangeRequest>(db, nameof(ChangeRequest.DedupeKey))!.Value;
        const string domain = "@example.az";
        string Email(int length) => new string('a', length - domain.Length) + domain;
        var longest = max - "email:".Length;

        var refused = await a.Desk().CreateEmployeeAsync(a.Timekeeper, NoPhone(a.W.B1, Email(longest + 1)), null, default);
        Assert.Equal(400, refused.Status);
        Assert.Equal("EmailTooLong", Json(refused.Body).GetProperty("error").GetString());

        var created = await a.Desk().CreateEmployeeAsync(a.Timekeeper, NoPhone(a.W.B1, Email(longest)), null, default);
        Assert.Equal(201, created.Status);
        Assert.Equal(max, a.Request(IdOf(created)).DedupeKey!.Length);
    }

    [Fact]
    public async Task With_a_phone_the_dedupe_key_is_the_phone_so_a_long_email_is_not_refused_for_it()
    {
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var email = new string('a', 230) + "@example.az";

        var created = await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1, email: email), null, default);
        Assert.Equal(201, created.Status);
        Assert.StartsWith("phone:", a.Request(IdOf(created)).DedupeKey);
    }

    [Fact]
    public async Task A_request_made_before_the_check_is_refused_at_approval_with_the_reason_and_stays_waiting()
    {
        // A request already in the queue with a name the column cannot take. It cannot be approved; the approver is
        // now told why («TextTooLong») instead of «ApplyFailed», and can reject it.
        using var a = await CreateAsync();
        await a.ActivateAsync(structure: a.S1);
        var id = IdOf(await a.Desk().CreateEmployeeAsync(a.Timekeeper, Person(a.W.B1), null, default));
        using (var db = a.Db())
        {
            var row = db.ChangeRequests.Single(r => r.Id == id);
            var payload = JsonSerializer.Deserialize<EmployeeCreatePayload>(row.Payload, ApprovalGate.Json)!;
            row.Payload = JsonSerializer.Serialize(
                payload with { FirstName = new string('A', 100), LastName = new string('B', 100) }, ApprovalGate.Json);
            db.SaveChanges();
        }

        var result = await a.Desk().ApproveAsync(a.Operator1, id, null, default);
        Assert.Equal(409, result.Status);
        Assert.Equal("ApplyRefused", Json(result.Body).GetProperty("error").GetString());
        Assert.Equal("TextTooLong", Json(result.Body).GetProperty("reason").GetString());
        Assert.Equal(ChangeRequestStatus.Pending, a.Request(id).Status);
        Assert.Empty(a.PeopleWithPhone("551234567"));
    }
}
