using AttendanceQR.Domain.Access;
using Xunit;
using static AttendanceQR.Domain.Access.AccessPermissions;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The role templates are a contract (docs/strukturlar-ve-selahiyyetler.md §3.4). The legacy manager role in
/// particular IS today's branch-manager access: a permission added to it is a power every manager in every
/// company gains overnight, and one taken out is a power they lose. So it is spelled out here in full.
/// </summary>
public class AccessTemplatesTests
{
    private static readonly string[] Reads =
    [
        DashboardView, AttendanceView, AttendancePhotosView, AttendanceExport, EmployeesView, EmployeesContactsView,
        DevicesView, FieldVisitsView, LeavesView, SchedulesView, PayrollView, LocationsView, StructuresView,
        AssignmentsView, EquipmentView, BillingView, DiagView,
    ];

    [Fact]
    public void Every_template_permission_is_in_the_catalogue_and_has_a_label()
    {
        foreach (var template in AccessRoleTemplates.All)
            Assert.All(template.Permissions, p => Assert.Contains(p, AccessPermissions.All));
        Assert.All(AccessPermissions.All, p => Assert.False(string.IsNullOrWhiteSpace(AccessPermissions.Labels[p])));
    }

    [Fact]
    public void The_company_administrator_holds_everything_and_is_locked()
    {
        var admin = AccessRoleTemplates.CompanyAdminTemplate;

        Assert.True(admin.IsLocked);
        Assert.Equal(AccessPermissions.All.Order(), admin.Permissions.Order());
    }

    [Fact]
    public void The_legacy_manager_is_exactly_todays_branch_manager()
    {
        string[] today =
        [
            DashboardView, AttendanceView, AttendancePhotosView, AttendanceExport, AttendanceCorrect, AttendanceFraud,
            EmployeesView, EmployeesContactsView, EmployeesCreate, EmployeesUpdate, EmployeesDeactivate,
            DevicesView, DevicesApprove, DevicesRevoke, DevicesShareGrant,
            FieldCheckInGrant, CheckInRulesOverride, FieldVisitsView, FieldVisitsManage,
            LeavesView, LeavesManage, SchedulesView, SchedulesManage, SchedulesAssign, PayrollView,
            LocationsView, LocationsUpdate, LocationsGeofenceManage, LocationsQrPrint,
            NotificationsStaffAlerts, TasksUse,
        ];

        Assert.Equal(today.Order(), AccessRoleTemplates.LegacyManagerTemplate.Permissions.Order());
        Assert.True(AccessRoleTemplates.LegacyManagerTemplate.PeersManageable);
        Assert.Equal([EmployeesChangePhone, EmployeesResetPin], AccessRoleTemplates.PinHelperTemplate.Permissions.Order());
    }

    [Fact]
    public void No_branch_or_structure_template_carries_a_company_only_permission()
    {
        foreach (var template in AccessRoleTemplates.All.Where(t => t.Key != AccessRoleTemplates.CompanyAdmin))
            Assert.DoesNotContain(template.Permissions, AccessPermissions.CompanyOnly.Contains);
    }

    [Fact]
    public void The_observer_only_reads()
    {
        Assert.All(AccessRoleTemplates.ObserverTemplate.Permissions, p => Assert.Contains(p, Reads));
    }

    [Theory]
    [InlineData(AttendanceCorrect)]
    [InlineData(FieldCheckInGrant)]
    [InlineData(CheckInRulesOverride)]
    [InlineData(AttendanceFraud)]
    [InlineData(AssignmentsManage)]
    public void Sensitive_work_is_not_in_the_new_templates_until_the_owner_adds_it(string permission)
    {
        // The owner's spec: correcting attendance, granting field check-in and security changes need their own
        // permission — the timekeeper and the structure operator start without them.
        Assert.DoesNotContain(permission, AccessRoleTemplates.TimekeeperTemplate.Permissions);
        Assert.DoesNotContain(permission, AccessRoleTemplates.StructureOperatorTemplate.Permissions);
        Assert.DoesNotContain(permission, AccessRoleTemplates.ObserverTemplate.Permissions);
    }

    [Fact]
    public void A_structure_operator_holds_everything_a_timekeeper_does()
    {
        // So an operator over a structure can manage the timekeepers of its branches (the dominance rule).
        Assert.Subset(AccessRoleTemplates.StructureOperatorTemplate.Permissions.ToHashSet(),
            AccessRoleTemplates.TimekeeperTemplate.Permissions.ToHashSet());
    }
}
