using AttendanceQR.Api;
using Xunit;

namespace AttendanceQR.Application.Tests;

/// <summary>
/// The name comparison itself, away from the database.
///
/// Its job is narrow and its failure modes are asymmetric: failing to match costs a link somebody can
/// make by hand, while matching the wrong person hangs one employee's laptops on another and nobody
/// ever goes looking. Every case below that involves doubt therefore checks that it DECLINES.
/// </summary>
public class PersonNameTests
{
    private static Guid? Resolve(string register, params string[] staff)
    {
        var people = staff.Select(n => (Id: Guid.NewGuid(), Name: n)).ToList();
        return PersonName.Resolve(register, people, p => p.Name, p => p.Id);
    }

    private static Guid? Expect(string register, string wanted, params string[] others)
    {
        var people = others.Select(n => (Id: Guid.NewGuid(), Name: n)).ToList();
        var target = (Id: Guid.NewGuid(), Name: wanted);
        people.Add(target);
        var got = PersonName.Resolve(register, people, p => p.Name, p => p.Id);
        Assert.Equal(target.Id, got);
        return got;
    }

    [Fact]
    public void Matches_across_the_two_conventions()
    {
        // The register keeps the official heading «Soyadı, adı, atasının adı»; the staff list holds
        // whatever was typed when the account was made. Comparing those as strings matched 0 of 80.
        Expect("Məmmədov Elçin Rəşid oğlu", "Elçin Məmmədov");
        Expect("Cavadzadə Şəbnəm Ədalət", "Şəbnəm Cavadzadə");
        Expect("Həsənova Günel Vaqif qızı", "Günel Həsənova");
    }

    [Fact]
    public void Matches_when_the_staff_list_is_the_one_written_in_full()
    {
        Expect("Məmmədov Elçin Rəşid oğlu", "Məmmədov Elçin Rəşid");
    }

    [Fact]
    public void Ignores_the_patronymic_marker()
    {
        // «oğlu» is in hundreds of names and identifies nobody, so it must not count as evidence.
        Assert.Equal(PersonName.Key("Məmmədov Elçin oğlu"), PersonName.Key("Elçin Məmmədov"));
    }

    [Fact]
    public void Declines_when_two_people_fit()
    {
        Assert.Null(Resolve("Məmmədov Elçin Rəşid oğlu", "Elçin Məmmədov", "Elçin Rəşid Məmmədov"));
    }

    [Fact]
    public void Declines_a_single_word_on_either_side()
    {
        // A surname is a family, not a person.
        Assert.Null(Resolve("Məmmədov Elçin Rəşid oğlu", "Məmmədov"));
        Assert.Null(Resolve("Məmmədov", "Elçin Məmmədov"));
    }

    [Fact]
    public void Declines_a_staff_name_carrying_a_word_the_register_does_not()
    {
        // Subset one way only. The register carries the patronymic and the staff list does not —
        // never the reverse — so an extra word on the staff side means a different person.
        Assert.Null(Resolve("Məmmədov Elçin", "Elçin Məmmədov Vaqif"));
    }

    [Fact]
    public void Declines_when_only_the_surname_is_shared()
    {
        Assert.Null(Resolve("Məmmədov Elçin Rəşid oğlu", "Aygün Məmmədova"));
    }

    [Fact]
    public void Is_not_broken_by_the_dotted_capital_i()
    {
        // 'İ'.ToLowerInvariant() is 'i' plus a COMBINING DOT ABOVE, and 'I' lowercases to a dotted
        // 'i' where Azerbaijani wants 'ı'. Two people typing the same name two ways would otherwise
        // never meet. Same guard the sheet's header matching carries.
        Expect("İsmayılov İlqar Iqbal oğlu", "İlqar İsmayılov");
        Assert.Equal(PersonName.Key("İLQAR İSMAYILOV"), PersonName.Key("İlqar İsmayılov"));
    }

    [Fact]
    public void Ignores_initials_and_punctuation()
    {
        // «Ə.» is a subset of half the company; a name reduced to initials must not match anyone.
        Assert.Null(Resolve("Məmmədov Elçin Rəşid oğlu", "Ə. Məmmədov"));
        Assert.Equal(PersonName.Key("Məmmədov, Elçin"), PersonName.Key("Elçin Məmmədov"));
    }

    [Fact]
    public void Gives_one_person_one_key_however_their_name_is_ordered()
    {
        Assert.Equal(PersonName.Key("Məmmədov Elçin"), PersonName.Key("Elçin  Məmmədov"));
        Assert.NotEqual(PersonName.Key("Məmmədov Elçin"), PersonName.Key("Məmmədov Elnur"));
    }

    [Fact]
    public void Has_an_empty_key_for_nothing()
    {
        Assert.Equal(string.Empty, PersonName.Key(null));
        Assert.Equal(string.Empty, PersonName.Key("   "));
        Assert.Null(Resolve("", "Elçin Məmmədov"));
    }
}
