// Bloq axtarışının saf funksiyaları (DOM yoxdur), blogFilter.ts işlədir.

// Azərbaycan hərfləri latın qarşılığına, «ё» rus «е»-yə çevrilir ki, «quraşdırma» və «qurasdirma» eyni tapılsın.
const LETTER_MAP: Record<string, string> = {
  'ə': 'e', 'ı': 'i', 'ö': 'o', 'ü': 'u', 'ç': 'c', 'ş': 's', 'ğ': 'g', 'ё': 'е',
}
const LETTER_PATTERN = /[əıöüçşğё]/g

const COMBINING_MARKS = /[̀-ͯ]/g

// 'Quraşdırma' → 'qurasdirma'. Kiçildirmə Azərbaycan qaydası ilədir (I→ı, İ→i).
export function normalizeSearch(text: string): string {
  return text
    .toLocaleLowerCase('az')
    .replace(LETTER_PATTERN, (letter) => LETTER_MAP[letter])
    .normalize('NFD')
    .replace(COMBINING_MARKS, '')
}

export function splitQuery(query: string): string[] {
  return normalizeSearch(query).split(/\s+/).filter(Boolean)
}

// Hər söz mətndə hissə kimi tapılmalıdır ('telefon' → 'telefona'). Boş sorğuya hər şey uyğundur.
export function matchesQuery(normalizedHaystack: string, tokens: string[]): boolean {
  return tokens.every((token) => normalizedHaystack.includes(token))
}
