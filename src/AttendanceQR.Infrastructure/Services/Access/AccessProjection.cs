using AttendanceQR.Domain.Access;
using AttendanceQR.Domain.Enums;

namespace AttendanceQR.Infrastructure.Services.Access;

/// <summary>
/// How the code that grew up around today's two roles reads a company switched to Enforced (Phase 5 of
/// docs/strukturlar-ve-selahiyyetler.md).
///
/// Every screen and most actions ask one question — «Admin or Manager?» — and take one of two shapes: the
/// company-wide one, and the manager's, narrowed to their branches and carrying no salary, no role, nobody
/// above them. The permission model does not replace those two shapes; it decides which one a person gets,
/// and how far the narrowed one reaches:
///
/// - the Company administrator role, company-wide → the company-wide shape (Admin);
/// - the permission this request stands for, anywhere → the narrowed shape (Manager), narrowed to where the
///   model says they hold it — a company-wide observer gets every branch, but still the narrowed shape, so a
///   right the role does not carry (a salary, a phone number, a selfie) never comes with the admin's shape;
/// - otherwise → plain staff (Employee), who see their own records and nothing else.
///
/// One rule, read by the request's role (AuthzEnforcedMiddleware), the reports
/// (LocationScopeRules.EffectiveRoleAsync) and the panel (GET /api/me/access → «companyAdmin»), so the screen,
/// the gate and the data cannot disagree about who somebody is.
/// </summary>
public static class AccessProjection
{
    /// <summary>Holds the locked Company administrator role over the whole company — the one role that is
    /// every right everywhere, and the only one that gets the admin's shape of a screen.</summary>
    public static bool IsCompanyAdmin(EffectiveAccess access) =>
        access.Grants.Any(g => g.TemplateKey == AccessRoleTemplates.CompanyAdmin && g.Scope == AccessScopeType.Company);

    /// <summary>The role today's code paths take for this person and this permission. See the class.</summary>
    public static EmployeeRole RoleFor(EffectiveAccess access, string permission) =>
        IsCompanyAdmin(access) ? EmployeeRole.Admin
        : access.Has(permission) ? EmployeeRole.Manager
        : EmployeeRole.Employee;
}
