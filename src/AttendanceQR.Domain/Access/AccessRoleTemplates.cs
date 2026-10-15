using static AttendanceQR.Domain.Access.AccessPermissions;

namespace AttendanceQR.Domain.Access;

/// <param name="Key">Stable identifier stored on the role (<c>AccessRole.TemplateKey</c>).</param>
/// <param name="IsLocked">Defined by the code, not the company: its permissions are re-synced from here on
/// every start and cannot be edited. Company administrator is locked so the way back in can never be
/// edited away; the two legacy roles are locked because they ARE today's access, and nothing but the code
/// that reads <c>Employee.Role</c> and <c>ManagedLocations</c> may say what that is.</param>
/// <param name="IsLegacy">Assigned by the system from today's data (Employee.Role, ManagedLocations), never by a
/// person — see AccessSeeder.</param>
/// <param name="PeersManageable">A holder of this role does not shield another holder who works at a branch
/// they share. Only the legacy manager role has it: it is today's colleague rule (2026-09-18 — a fellow
/// manager at one of my branches), kept exactly so nobody loses it in the move. Every new role uses the
/// strict rule: you only manage people whose every power you hold yourself.</param>
public sealed record AccessRoleTemplate(
    string Key, string Name, string Description, IReadOnlyList<string> Permissions,
    bool IsLocked, bool IsLegacy, bool PeersManageable);

/// <summary>
/// The roles every company starts with. Three are migration roles that reproduce today's access exactly;
/// three are the Bakı Abadlıq starting templates for the new way of working, unassigned until the owner
/// gives them to people. docs/strukturlar-ve-selahiyyetler.md §3.4.
/// </summary>
public static class AccessRoleTemplates
{
    public const string CompanyAdmin = "company_admin";
    public const string LegacyManager = "legacy_manager";
    public const string PinHelper = "pin_helper";
    public const string StructureOperator = "structure_operator";
    public const string Timekeeper = "timekeeper";
    public const string Observer = "observer";

    public static readonly AccessRoleTemplate CompanyAdminTemplate = new(
        CompanyAdmin, "Şirkət administratoru",
        "Şirkət üzrə bütün hüquqlar. Silinmir və dəyişdirilmir — hesab bərpasının yolu budur.",
        AccessPermissions.All.Order().ToList(), IsLocked: true, IsLegacy: false, PeersManageable: false);

    /// <summary>
    /// Exactly what a branch manager can do today, branch by branch — the «Menecer» column of §3.3, with the
    /// one change the owner decided (2026-10-15): shifts are managed only where the manager's scope reaches,
    /// so a company-wide shift needs company scope. The company-wide PIN and number reset is the separate
    /// <see cref="PinHelper"/> role, because its reach is the company, not the branch.
    /// </summary>
    public static readonly AccessRoleTemplate LegacyManagerTemplate = new(
        LegacyManager, "Filial meneceri (köhnə qayda)",
        "Bugünkü menecer hüquqları, öz filiallarında. Keçid rolu: adamlar yeni rollara keçdikcə boşalır.",
        [
            DashboardView, AttendanceView, AttendancePhotosView, AttendanceExport, AttendanceCorrect, AttendanceFraud,
            EmployeesView, EmployeesContactsView, EmployeesCreate, EmployeesUpdate, EmployeesDeactivate,
            DevicesView, DevicesApprove, DevicesRevoke, DevicesShareGrant,
            FieldCheckInGrant, CheckInRulesOverride, FieldVisitsView, FieldVisitsManage,
            LeavesView, LeavesManage, SchedulesView, SchedulesManage, SchedulesAssign, PayrollView,
            LocationsView, LocationsUpdate, LocationsGeofenceManage, LocationsQrPrint,
            NotificationsStaffAlerts, TasksUse,
        ],
        IsLocked: true, IsLegacy: true, PeersManageable: true);

    /// <summary>Today's company-wide reach of a manager (the owner's call of 2026-09-14): a new PIN or login
    /// number for any plain employee in the company, and a colleague at one of their own branches.</summary>
    public static readonly AccessRoleTemplate PinHelperTemplate = new(
        PinHelper, "PIN köməkçisi",
        "Şirkətin istənilən adi işçisinə yeni PIN və ya giriş nömrəsi (bugünkü menecer imkanı).",
        [EmployeesResetPin, EmployeesChangePhone],
        IsLocked: true, IsLegacy: true, PeersManageable: false);

    public static readonly AccessRoleTemplate StructureOperatorTemplate = new(
        StructureOperator, "Struktur operatoru",
        "Strukturun bütün filiallarında gündəlik kompüter işləri: işçilər, növbələr, PIN, telefon təsdiqi, hesabatlar.",
        [
            DashboardView, AttendanceView, AttendancePhotosView, AttendanceExport,
            EmployeesView, EmployeesContactsView, EmployeesCreate, EmployeesUpdate, EmployeesDeactivate,
            EmployeesResetPin, EmployeesChangePhone,
            DevicesView, DevicesApprove, DevicesRevoke,
            SchedulesView, SchedulesManage, SchedulesAssign, LeavesView, LeavesManage,
            LocationsView, LocationsQrPrint, FieldVisitsView, FieldVisitsManage,
            AssignmentsView, NotificationsStaffAlerts,
        ],
        IsLocked: false, IsLegacy: false, PeersManageable: false);

    public static readonly AccessRoleTemplate TimekeeperTemplate = new(
        Timekeeper, "Tabelçi",
        "Təyin olunduğu filiallarda: işçilər, PIN, növbələr, davamiyyəti izləmək, hesabatlar.",
        [
            DashboardView, AttendanceView, AttendanceExport,
            EmployeesView, EmployeesContactsView, EmployeesCreate, EmployeesUpdate, EmployeesResetPin,
            DevicesView, DevicesApprove,
            SchedulesView, SchedulesManage, SchedulesAssign, LeavesView, LeavesManage,
            FieldVisitsView, NotificationsStaffAlerts,
        ],
        IsLocked: false, IsLegacy: false, PeersManageable: false);

    public static readonly AccessRoleTemplate ObserverTemplate = new(
        Observer, "Müşahidəçi",
        "Yalnız baxır: davamiyyət, hesabatlar, filialların vəziyyəti. Heç nə dəyişmir.",
        [DashboardView, AttendanceView, AttendanceExport, EmployeesView, LeavesView, SchedulesView, LocationsView, FieldVisitsView],
        IsLocked: false, IsLegacy: false, PeersManageable: false);

    public static readonly IReadOnlyList<AccessRoleTemplate> All =
    [
        CompanyAdminTemplate, LegacyManagerTemplate, PinHelperTemplate,
        StructureOperatorTemplate, TimekeeperTemplate, ObserverTemplate,
    ];
}
