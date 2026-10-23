/**
 * Telefon nömrəsi ilə bağlı kiçik köməkçilər: təmizləmək, boşluqlarla göstərmək və yoxlamaq.
 *
 * Qayda: proqram nömrəni HƏMİŞƏ boşluqsuz rəqəmlər kimi saxlayır ("0501234567"), boşluqları isə yalnız
 * ekranda göstərəndə əlavə edir ("050 123 45 67"). Belə olanda yoxlama, serverə göndərmə və müqayisə
 * sadə qalır — boşluq, "+" və mötərizə heç yerə qarışmır.
 *
 * İki variant var:
 * - "local"         — 0XX XXX XX XX, 10 rəqəm, 0 ilə başlayır (giriş və "PIN-i unutdum" səhifələri).
 * - "international" — +994 ayrıca göstərilir, istifadəçi 9 rəqəm yazır: XX XXX XX XX (qeydiyyat).
 *
 * Server hər iki yazılışı qəbul edir: backend nömrənin son 9 rəqəmini götürüb müqayisə edir
 * (src/AttendanceQR.Api/PhoneNumbers.cs).
 */

export type PhoneVariant = 'local' | 'international'

const COUNTRY_CODE = '994'
const LOCAL_LENGTH = 10 // 0XX XXX XX XX
const INTERNATIONAL_LENGTH = 9 // XX XXX XX XX (+994 ayrıca yazılır)

// Rəqəmlər hansı qruplara bölünüb göstərilir: 050 123 45 67 → [3, 3, 2, 2]
const LOCAL_GROUPS = [3, 3, 2, 2]
const INTERNATIONAL_GROUPS = [2, 3, 2, 2]

/** Mətndən rəqəm olmayan hər şeyi (boşluq, "+", "-", hərf) silir: "+994 (50) 123" → "99450123". */
export function onlyDigits(text: string): string {
  return text.replace(/\D/g, '')
}

/** Bu variantda nömrə ən çox neçə rəqəm ola bilər. */
export function phoneMaxLength(variant: PhoneVariant): number {
  return variant === 'local' ? LOCAL_LENGTH : INTERNATIONAL_LENGTH
}

/**
 * İstifadəçinin yazdığını və ya yapışdırdığını təmiz rəqəmlərə çevirir və artıq rəqəmləri kəsir.
 *
 * local:         "+994 50 123 45 67" → "0501234567"  (994 silinir, başına 0 qoyulur)
 * international: "+994 50 123 45 67" → "501234567"   (994 silinir)
 *                "050 123 45 67"     → "501234567"   (başdakı 0 silinir)
 */
export function cleanPhone(raw: string, variant: PhoneVariant): string {
  let digits = onlyDigits(raw)

  if (variant === 'local') {
    // Yerli nömrə həmişə 0 ilə başlayır, ona görə 994 ilə başlayan dəyər mütləq ölkə kodudur.
    if (digits.startsWith(COUNTRY_CODE)) {
      const rest = digits.slice(COUNTRY_CODE.length)
      // "+994 050..." kimi yazılıbsa, 0 artıq var — ikinci 0 qoymuruq.
      digits = rest.startsWith('0') ? rest : '0' + rest
    }
    return digits.slice(0, LOCAL_LENGTH)
  }

  // international
  // 994-ü yalnız rəqəm 9-dan ÇOX olanda silirik. Səbəb: "99 4XX XX XX" (Bakcell 099) kimi nömrə də 994
  // ilə başlayır — istifadəçi onu yazarkən ilk üç rəqəmi silsəydik, nömrəni heç vaxt yaza bilməzdi.
  if (digits.length > INTERNATIONAL_LENGTH && digits.startsWith(COUNTRY_CODE)) {
    digits = digits.slice(COUNTRY_CODE.length)
  }
  // Beynəlxalq yazılışda nömrə 0 ilə başlamır, ona görə yerli formadakı 0-ı silirik.
  if (digits.startsWith('0')) {
    digits = digits.slice(1)
  }
  return digits.slice(0, INTERNATIONAL_LENGTH)
}

/** Boşluqsuz rəqəmləri qruplara bölüb göstərir: "0501234567" → "050 123 45 67". Yarımçıq nömrə də olur: "05012" → "050 12". */
export function formatPhone(digits: string, variant: PhoneVariant): string {
  const groups = variant === 'local' ? LOCAL_GROUPS : INTERNATIONAL_GROUPS
  const parts: string[] = []
  let start = 0
  for (const size of groups) {
    if (start >= digits.length) break
    parts.push(digits.slice(start, start + size))
    start += size
  }
  return parts.join(' ')
}

/** Nömrə tamdırmı: local — 10 rəqəm və 0 ilə başlayır; international — 9 rəqəm. */
export function isValidPhone(digits: string, variant: PhoneVariant): boolean {
  if (variant === 'local') {
    return digits.length === LOCAL_LENGTH && digits.startsWith('0')
  }
  return digits.length === INTERNATIONAL_LENGTH && !digits.startsWith('0')
}

/** Sahənin altında göstəriləcək xəta mətni, nömrə düzgündürsə null. */
export function phoneError(digits: string, variant: PhoneVariant): string | null {
  if (isValidPhone(digits, variant)) return null
  if (variant === 'local') {
    // 10 rəqəm yazılıb, amma 0 ilə başlamır — "10 rəqəm olmalıdır" burada çaşdırardı.
    if (digits.length === LOCAL_LENGTH) return 'Nömrə 0 ilə başlamalıdır'
    return 'Nömrə 10 rəqəm olmalıdır'
  }
  return 'Nömrə 9 rəqəm olmalıdır'
}
