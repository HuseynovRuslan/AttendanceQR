namespace AttendanceQR.Domain.Access;

/// <summary>
/// The permission catalogue — every key a role can carry. Keys are internal; the panel shows the Azerbaijani
/// label. The full rationale, and which of today's endpoints each key stands for, is in
/// docs/strukturlar-ve-selahiyyetler.md §3.3.
///
/// A key never changes its meaning once shipped: roles store keys as text, so renaming one silently takes
/// the permission away from every role that had it. Retire a key by leaving it here unused instead.
/// </summary>
public static class AccessPermissions
{
    public const string DashboardView = "dashboard.view";

    public const string AttendanceView = "attendance.view";
    public const string AttendancePhotosView = "attendance.photos.view";
    public const string AttendanceExport = "attendance.export";
    public const string AttendanceCorrect = "attendance.correct";
    public const string AttendanceFraud = "attendance.fraud";
    public const string AttendanceMaintenance = "attendance.maintenance";

    public const string EmployeesView = "employees.view";
    public const string EmployeesContactsView = "employees.contacts.view";
    public const string EmployeesCreate = "employees.create";
    public const string EmployeesImport = "employees.import";
    public const string EmployeesUpdate = "employees.update";
    public const string EmployeesDeactivate = "employees.deactivate";
    public const string EmployeesDelete = "employees.delete";
    public const string EmployeesResetPin = "employees.reset_pin";
    public const string EmployeesChangePhone = "employees.change_phone";
    public const string EmployeesReinvite = "employees.reinvite";
    public const string EmployeesSalary = "employees.salary";
    public const string EmployeesReferencePhoto = "employees.reference_photo";
    public const string EmployeesResetAttendance = "employees.reset_attendance";

    public const string DevicesView = "devices.view";
    public const string DevicesApprove = "devices.approve";
    public const string DevicesRevoke = "devices.revoke";
    public const string DevicesShareGrant = "devices.share_grant";

    public const string FieldCheckInGrant = "field_checkin.grant";
    public const string CheckInRulesOverride = "checkin_rules.override";
    public const string FieldVisitsView = "field_visits.view";
    public const string FieldVisitsManage = "field_visits.manage";
    public const string FieldVisitsDiscard = "field_visits.discard";

    public const string LeavesView = "leaves.view";
    public const string LeavesManage = "leaves.manage";

    public const string SchedulesView = "schedules.view";
    public const string SchedulesManage = "schedules.manage";
    public const string SchedulesAssign = "schedules.assign";

    public const string PayrollView = "payroll.view";
    public const string PayrollExport = "payroll.export";

    public const string LocationsView = "locations.view";
    public const string LocationsCreate = "locations.create";
    public const string LocationsActivate = "locations.activate";
    public const string LocationsUpdate = "locations.update";
    public const string LocationsGeofenceManage = "locations.geofence_manage";
    public const string LocationsQrPrint = "locations.qr_print";
    public const string LocationsQrInvalidate = "locations.qr_invalidate";

    public const string StructuresView = "structures.view";
    public const string StructuresManage = "structures.manage";

    public const string AssignmentsView = "assignments.view";
    public const string AssignmentsManage = "assignments.manage";
    /// <summary>Giving a role to somebody who works OUTSIDE your own area. The owner's rule (2026-10-15): by
    /// default an assigner reaches only their own people; reaching further is a separate, higher right.</summary>
    public const string AssignmentsCrossArea = "assignments.cross_area";
    public const string RolesManage = "roles.manage";

    public const string NotificationsStaffAlerts = "notifications.staff_alerts";
    public const string TasksUse = "tasks.use";

    public const string HolidaysManage = "holidays.manage";
    public const string PositionsManage = "positions.manage";
    public const string AnnouncementsManage = "announcements.manage";
    public const string VotesManage = "votes.manage";
    public const string EquipmentView = "equipment.view";
    public const string EquipmentManage = "equipment.manage";
    public const string BillingView = "billing.view";
    public const string DiagView = "diag.view";

    /// <summary>Key → what the panel calls it.</summary>
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [DashboardView] = "İdarəetmə paneli",
        [AttendanceView] = "Davamiyyəti görmək",
        [AttendancePhotosView] = "Giriş şəkillərini görmək",
        [AttendanceExport] = "Excel çıxarışları",
        [AttendanceCorrect] = "Davamiyyətə əl ilə düzəliş",
        [AttendanceFraud] = "Saxtakarlıq: günü ləğv etmək, foto xəbərdarlığı",
        [AttendanceMaintenance] = "Xülasələri yenidən hesablamaq",
        [EmployeesView] = "İşçiləri görmək",
        [EmployeesContactsView] = "Telefon və e-poçtu görmək",
        [EmployeesCreate] = "İşçi əlavə etmək",
        [EmployeesImport] = "Toplu idxal və dəvət",
        [EmployeesUpdate] = "İşçi məlumatlarını dəyişmək",
        [EmployeesDeactivate] = "İşdən çıxartmaq və qaytarmaq",
        [EmployeesDelete] = "İşçini silmək",
        [EmployeesResetPin] = "PIN sıfırlamaq",
        [EmployeesChangePhone] = "Giriş nömrəsini dəyişmək",
        [EmployeesReinvite] = "Dəvət linkini yeniləmək",
        [EmployeesSalary] = "Maaşı təyin etmək",
        [EmployeesReferencePhoto] = "İstinad şəklini sıfırlamaq",
        [EmployeesResetAttendance] = "Davamiyyət tarixçəsini silmək",
        [DevicesView] = "Bağlı telefonları görmək",
        [DevicesApprove] = "Yeni telefon tələbini təsdiqləmək",
        [DevicesRevoke] = "Telefonu ləğv etmək",
        [DevicesShareGrant] = "Ortaq telefon icazəsi",
        [FieldCheckInGrant] = "Səyyar giriş hüququ vermək",
        [CheckInRulesOverride] = "Fərdi QR-siz / GPS istisnası",
        [FieldVisitsView] = "Sahə ziyarətlərini görmək",
        [FieldVisitsManage] = "Sahə ziyarətlərini idarə etmək",
        [FieldVisitsDiscard] = "Tamamlanmış ziyarəti silmək",
        [LeavesView] = "Məzuniyyətləri görmək",
        [LeavesManage] = "Məzuniyyət yazmaq",
        [SchedulesView] = "Növbələri görmək",
        [SchedulesManage] = "Növbə yaratmaq və dəyişmək",
        [SchedulesAssign] = "Növbə təyin etmək",
        [PayrollView] = "Maaş cədvəli",
        [PayrollExport] = "Maaş Excel-i",
        [LocationsView] = "Filialları görmək",
        [LocationsCreate] = "Filial yaratmaq",
        [LocationsActivate] = "Filialı aktivləşdirmək və silmək",
        [LocationsUpdate] = "Filial ayarları (ad, saat, günlər)",
        [LocationsGeofenceManage] = "GPS dairəsi, QR-siz rejim, GPS divarı",
        [LocationsQrPrint] = "QR posterini çap etmək",
        [LocationsQrInvalidate] = "QR-u etibarsız etmək",
        [StructuresView] = "Strukturları görmək",
        [StructuresManage] = "Strukturları idarə etmək",
        [AssignmentsView] = "Kimin hansı hüququ olduğunu görmək",
        [AssignmentsManage] = "Rol təyin etmək",
        [AssignmentsCrossArea] = "Başqa sahənin işçisinə rol vermək",
        [RolesManage] = "Rolları idarə etmək",
        [NotificationsStaffAlerts] = "İşçi problemi bildirişləri",
        [TasksUse] = "Tapşırıqlar lövhəsi",
        [HolidaysManage] = "Bayram və qeyri-iş günləri",
        [PositionsManage] = "Vəzifə kataloqu",
        [AnnouncementsManage] = "Elanlar",
        [VotesManage] = "«Ayın işçisi»",
        [EquipmentView] = "Kompüter reyestrini görmək",
        [EquipmentManage] = "Kompüter reyestrini idarə etmək",
        [BillingView] = "Abunə və ödənişlər",
        [DiagView] = "Diaqnostika",
    };

    public static readonly IReadOnlySet<string> All = Labels.Keys.ToHashSet();

    /// <summary>
    /// Meaningful only for the whole company — a holiday, the job-title list, the subscription — so they are
    /// granted at company scope or not at all. A branch-scoped role carrying one is refused when it is saved.
    /// </summary>
    public static readonly IReadOnlySet<string> CompanyOnly = new HashSet<string>
    {
        AttendanceMaintenance, EmployeesResetAttendance, LocationsCreate, LocationsActivate, StructuresManage,
        RolesManage, AssignmentsCrossArea, HolidaysManage, PositionsManage, AnnouncementsManage, VotesManage,
        EquipmentView, EquipmentManage, BillingView, DiagView,
    };

    /// <summary>
    /// The rights ABOUT rights. Left out when two roles' levels are compared (AccessRules.CanGrant): an
    /// assigner handing out their own daily powers minus only the right to assign is still appointing their
    /// equal — the owner's rule 3 of 2026-10-15 forbids exactly that below company level.
    /// </summary>
    public static readonly IReadOnlySet<string> Meta = new HashSet<string>
    {
        AssignmentsView, AssignmentsManage, AssignmentsCrossArea, RolesManage,
    };
}
