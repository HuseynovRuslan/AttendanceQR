/**
 * Formalardakı sadə yoxlamalar (telefon yoxlaması ayrıca lib/phone.ts-dədir).
 * Server yenə öz yoxlamasını edir — bunlar yalnız istifadəçiyə xətanı sorğudan ƏVVƏL, sahənin
 * altında göstərmək üçündür.
 */

/** E-poçtda "@" var və ondan sonra nöqtə gəlir: "ad@sirket.az" — düz, "ad@sirket" — səhv. */
export function isValidEmail(email: string): boolean {
  const text = email.trim()
  const atIndex = text.indexOf('@')
  if (atIndex < 1) return false // "@" yoxdur və ya ən əvvəldədir
  const dotIndex = text.indexOf('.', atIndex)
  // Nöqtə "@"-dan dərhal sonra və ya ən sonda olmamalıdır: "ad@.az" və "ad@sirket." səhvdir.
  return dotIndex > atIndex + 1 && dotIndex < text.length - 1
}

/**
 * Ad və soyad üçün: ilk simvol hərfdir, sonra hərf, boşluq, apostrof (') və ya defis (-) ola bilər.
 *
 * \p{L} — istənilən əlifbanın hərfi deməkdir, Azərbaycan hərfləri (ə, ı, ö, ü, ğ, ş, ç) də daxil.
 * Sadə [a-zA-Z] bunları buraxmazdı. Sondakı "u" bayrağı (Unicode) \p{L}-in işləməsi üçün lazımdır.
 * "\-" — mötərizə içində defis adətən aralıq bildirir (a-z), qabağındakı "\" onu adi defis edir.
 * Nümunələr: "Ləman", "Məmməd-Əli", "Ma'sumə" — düz; "Ali2", "-Ali", "@" — səhv.
 */
const PERSON_NAME = /^[\p{L}][\p{L} '\-]*$/u
const MIN_NAME_LENGTH = 2

/** Ad/soyad düzgündürsə null, deyilsə xəta mətni. fieldName: "Ad" və ya "Soyad". */
export function personNameError(value: string, fieldName: string): string | null {
  const text = value.trim()
  if (text.length < MIN_NAME_LENGTH) return `${fieldName} ən azı ${MIN_NAME_LENGTH} hərf olmalıdır`
  if (!PERSON_NAME.test(text)) return `${fieldName} yalnız hərflərdən ibarət olmalıdır`
  return null
}
