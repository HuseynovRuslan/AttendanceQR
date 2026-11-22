using AttendanceQR.Domain.Entities;
using AttendanceQR.Infrastructure.Security;

namespace AttendanceQR.Api;

/// <summary>
/// Lifts every login lockout an account can be under. Called by every path that resets an existing
/// person's PIN: the admin card and the bulk reset, the «PIN-i unutdum» queue, the manager desk, the
/// platform console's support reset, and the self-service face reset.
///
/// One account is locked under up to FOUR keys: its email and its phone, each in the company's own
/// namespace (the subdomain login) and in the tenant-less one (app-login, which is what the Android app
/// and app.qrlog.az always use). The reset paths used to clear only the first two, so somebody who had
/// locked themselves out in the app — the usual reason anyone files «PIN-i unutdum» — was given a fresh
/// PIN that the app then refused with «Çox sayda cəhd». Once lockouts started growing with each repeat
/// (5, 15, 60 minutes) that was up to an hour in which the person an admin had just helped could not
/// sign in, and so could not check in. Clearing them all in one place is what keeps a new reset path,
/// or a new login namespace, from forgetting one again.
///
/// A key is cleared only for an identifier the account actually has: an empty one is the bucket every
/// blank login form spends from, and is nobody's to reset.
/// </summary>
public static class LoginLockouts
{
    public static void ClearFor(ILoginLockoutStore store, Employee employee)
    {
        foreach (var identifier in new[] { employee.Email, employee.PhoneNumber })
        {
            if (string.IsNullOrWhiteSpace(identifier))
                continue;
            // The account's own company — the key the subdomain login spent, whichever company's screen
            // (or the platform console) the reset was made from.
            store.RecordSuccess(LoginIdentity.LockoutKey(employee.TenantId, identifier));
            store.RecordSuccess(LoginIdentity.AppLoginKey(identifier));
        }
    }
}
