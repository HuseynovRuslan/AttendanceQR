using System.Reflection;
using AttendanceQR.Api.Contracts;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// An admin must not be able to lock themselves out of their own company.
///
/// This is not hypothetical: CleanFix's only admin set their own account to "Deaktiv", login started
/// refusing them (it has always checked IsActive), and a 13-person tenant was left with zero admins
/// who could sign in — unrecoverable from inside, it took a hand-written UPDATE. Delete was already
/// guarded with CannotDeleteSelf; Update was not, and deactivating is the worse outcome of the two
/// because it is silent.
///
/// AdminController.Update needs a DbContext to test directly, which this suite has no harness for.
/// What is pinned here is the contract the guard depends on — the request must carry both fields, so
/// the controller can compare them against the caller's own record.
/// </summary>
public class SelfLockoutGuardTests
{
    [Theory]
    [InlineData("IsActive")]
    [InlineData("Role")]
    public void The_update_request_carries_the_fields_that_can_lock_an_admin_out(string name)
    {
        // If either is ever dropped from the contract, the guard silently has nothing to check.
        var parameter = typeof(EmployeeUpdateRequest)
            .GetConstructors().Single()
            .GetParameters()
            .SingleOrDefault(p => p.Name == name);

        Assert.NotNull(parameter);
    }

    [Fact]
    public void IsActive_is_not_optional_so_an_omitted_field_cannot_read_as_deactivate()
    {
        // A defaulted bool would be `false` — meaning a caller that simply forgot the field would
        // deactivate the employee. It must stay required.
        var isActive = typeof(EmployeeUpdateRequest)
            .GetConstructors().Single()
            .GetParameters()
            .Single(p => p.Name == "IsActive");

        Assert.False(isActive.IsOptional);
        Assert.Equal(typeof(bool), isActive.ParameterType);
    }
}
