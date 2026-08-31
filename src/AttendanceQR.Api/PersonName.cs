namespace AttendanceQR.Api;

/// <summary>
/// Matching a name written one way against the same person written another way.
///
/// The equipment register and the staff list do not agree about how to write a person, and never
/// will: the register keeps the official column heading "Soyadı, adı, atasının adı" and writes
/// «Məmmədov Elçin Rəşid oğlu», while the staff list holds whatever was typed when the account was
/// made — usually «Elçin Məmmədov». Comparing those for equality can never succeed, and it never did:
/// on the live register, 80 rows out of 80 came back unmatched, which meant the whole point of the
/// link — a person's kit showing on their own profile — did nothing for anybody.
///
/// So the comparison is on the SET of name words, not the string: order stops mattering, and an extra
/// word on the register's side (the patronymic) stops breaking it. What is deliberately NOT done is
/// fuzzy matching — no edit distance, no prefixes. Every rule here either matches a person exactly or
/// declines, because a near-miss hangs one person's laptops on another and nobody goes looking for a
/// link that is quietly wrong.
/// </summary>
public static class PersonName
{
    /// <summary>«… oğlu» / «… qızı» — a patronymic marker, not part of anyone's name.</summary>
    private static readonly HashSet<string> Patronymic =
        new(StringComparer.Ordinal) { "oğlu", "oglu", "ogli", "oglı", "qızı", "qizi", "kizi", "kızı" };

    private static readonly char[] Separators = [' ', ',', '.', '\t', '-', ' '];

    /// <summary>
    /// Lowercase, Azerbaijani-safe.
    ///
    /// <c>ToLowerInvariant</c> alone is wrong in both directions here: it turns «İ» into 'i' plus a
    /// COMBINING DOT ABOVE, and it turns «I» into a dotted 'i' where Azerbaijani wants the dotless
    /// 'ı'. So «Ilqar» and «İlqar» — the same person, typed by two people — fold to different
    /// strings and never match. The sheet importer carries the same guard for the same reason.
    /// </summary>
    public static string Fold(string text)
        => text.Replace('İ', 'i').Replace('I', 'ı').ToLowerInvariant().Replace("̇", string.Empty);

    /// <summary>
    /// A person's name as the set of words that identify them.
    ///
    /// Single letters go — an initial carries no evidence and would make «Ə. Məmmədov» a subset of
    /// half the company. Patronymic markers go for the same reason: «oğlu» appears in hundreds of
    /// names and identifies nobody.
    /// </summary>
    public static HashSet<string> Parts(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return new HashSet<string>(StringComparer.Ordinal);

        return Fold(name)
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 1 && !Patronymic.Contains(w))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// A stable string for the same person however their name is ordered — the words, sorted.
    ///
    /// Used to recognise a register row as one already in the table. Two people who genuinely share
    /// a name share a key, which is why the import pairs this with the row number rather than
    /// trusting it alone.
    /// </summary>
    public static string Key(string? name) => string.Join(' ', Parts(name).OrderBy(w => w, StringComparer.Ordinal));

    /// <summary>
    /// The one employee this register line names, or null.
    ///
    /// The staff name has to be a SUBSET of the register name, which is the asymmetry the data has:
    /// the register carries the patronymic and the staff list does not, never the other way round.
    /// «Elçin Məmmədov» ⊆ «Məmmədov Elçin Rəşid» matches; «Elçin Məmmədov Vaqif» ⊄ it does not.
    ///
    /// Two candidates means no answer, not a guess. Two brothers in the staff list — «Elçin
    /// Məmmədov» and «Elçin Rəşid Məmmədov» — are exactly the case where a wrong link is worse than
    /// an empty one, and both would otherwise be subsets of the same register line.
    ///
    /// Both sides need at least two words. One word is a surname, and a surname is a family, not a
    /// person.
    /// </summary>
    public static Guid? Resolve<T>(string? registerName, IEnumerable<T> staff,
        Func<T, string?> nameOf, Func<T, Guid> idOf)
    {
        var register = Parts(registerName);
        if (register.Count < 2) return null;

        Guid? found = null;
        foreach (var person in staff)
        {
            var parts = Parts(nameOf(person));
            if (parts.Count < 2 || !parts.IsSubsetOf(register)) continue;
            if (found is not null) return null; // ambiguous — decline rather than pick one
            found = idOf(person);
        }

        return found;
    }
}
