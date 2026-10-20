// Bloq axtarışının saf funksiyaları (DOM yoxdur): həm brauzer skripti (blogFilter.ts), həm də Node
// ilə sınaq eyni kodu işlədir.

// Hərf xəritəsi: Azərbaycan hərfləri latın qarşılığına, «ё» rus «е»-yə çevrilir ki, «quraşdırma» və
// «qurasdirma» eyni axtarılsın. Açar hərflər artıq kiçikdir (normalizeSearch əvvəlcə kiçildir).
const LETTER_MAP: Record<string, string> = {
  'ə': 'e', 'ı': 'i', 'ö': 'o', 'ü': 'u', 'ç': 'c', 'ş': 's', 'ğ': 'g', 'ё': 'е',
}
const LETTER_PATTERN = /[əıöüçşğё]/g

// Kombinasiya işarələri (NFD ayrılışından sonra qalan vurğu və s.): ̀–ͯ aralığı.
const COMBINING_MARKS = /[̀-ͯ]/g

// Mətni müqayisə üçün hazırlayır: kiçik hərf (Azərbaycan qaydası: I→ı, İ→i), xüsusi hərflər latına,
// vurğular silinir. Nümunə: 'Quraşdırma' → 'qurasdirma'.
export function normalizeSearch(text: string): string {
  return text
    .toLocaleLowerCase('az')
    .replace(LETTER_PATTERN, (letter) => LETTER_MAP[letter])
    .normalize('NFD')
    .replace(COMBINING_MARKS, '')
}

// Sorğunu normallaşdırıb boşluqla sözlərə bölür (boş sözlər atılır).
export function splitQuery(query: string): string[] {
  return normalizeSearch(query).split(/\s+/).filter(Boolean)
}

// Hər söz normallaşmış mətndə hissə kimi tapılmalıdır ('telefon' → 'telefona' uyğundur).
// Söz yoxdursa (boş sorğu) hər şey uyğundur.
export function matchesQuery(normalizedHaystack: string, tokens: string[]): boolean {
  return tokens.every((token) => normalizedHaystack.includes(token))
}
