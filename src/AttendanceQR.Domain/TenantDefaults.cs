namespace AttendanceQR.Domain;

/// <summary>
/// The single tenant that existed before multi-tenancy (Bakı Abadlıq). Phase 0 backfills every
/// pre-existing row to this id; kept as a stable constant for the migration seed and Phase-1 fallbacks.
/// </summary>
public static class TenantDefaults
{
    public static readonly Guid BakiAbadligId = new("00000000-0000-0000-0000-00000000ba01");
    public const string BakiAbadligSlug = "bax";
}
