import { useRef, type ChangeEvent, type ClipboardEvent, type KeyboardEvent } from 'react'
import { applyBackspace, applyDigit, applyPaste } from './pinDigits'
import { onlyDigits } from '../lib/phone'

/**
 * Hər rəqəm üçün ayrıca xana: qeydiyyatda PIN (4 xana, gizli) və SMS kodu (6 xana) üçün.
 * Harada: RegisterPage. (Girişdəki PinInput ayrıca qalır — ona toxunmuruq.)
 * Props: id — birinci xananın id-si (label və fokus üçün); length — xana sayı; value — yazılmış rəqəmlər;
 * onChange; masked — rəqəmlər nöqtə kimi görünsün; label — ekran oxuyucu üçün ad ("PIN" → "PIN, 2-ci rəqəm");
 * autoComplete — birinci xanaya ("one-time-code", "new-password"); hasError; errorId — xəta mətninin id-si.
 * İşləmə: rəqəm yazanda növbəti xanaya keçir, Backspace əvvəlkinə qayıdır, yapışdırılan kod xanalara
 * paylanır. Qərarları pinDigits.ts verir (testləri var), PinInput da eyni faylı işlədir.
 */
type OtpInputProps = {
  id: string
  length: number
  value: string
  onChange: (next: string) => void
  masked?: boolean
  label: string
  autoComplete?: string
  hasError?: boolean
  errorId?: string
}

// Azərbaycan dilində sıra sayının şəkilçisi səsin ahənginə görə dəyişir: 1-ci, 3-cü, 6-cı, 9-cu.
const ORDINAL_SUFFIX: Record<number, string> = {
  1: 'ci', 2: 'ci', 3: 'cü', 4: 'cü', 5: 'ci', 6: 'cı', 7: 'ci', 8: 'ci', 9: 'cu', 10: 'cu',
}

/** Xananın id-si: birincisi elə `id`-dir (label onu göstərir), qalanları `id-2`, `id-3`… */
function boxId(id: string, index: number): string {
  return index === 0 ? id : `${id}-${index + 1}`
}

export function OtpInput({
  id,
  length,
  value,
  onChange,
  masked = false,
  label,
  autoComplete = 'off',
  hasError = false,
  errorId,
}: OtpInputProps) {
  // useRef — render-lər arasında saxlanılan, amma dəyişəndə ekranı yenidən çəkdirməyən qutu.
  // Burada xanaların özlərini (DOM elementlərini) saxlayırıq ki, fokusu birindən digərinə keçirək.
  const boxes = useRef<(HTMLInputElement | null)[]>([])

  const digits: string[] = []
  for (let i = 0; i < length; i++) {
    digits.push(value[i] ?? '')
  }

  function focusBox(index: number) {
    boxes.current[index]?.focus()
  }

  function handleChange(index: number, event: ChangeEvent<HTMLInputElement>) {
    const typed = onlyDigits(event.target.value)
    const previous = digits[index]

    // Bir xanaya birdən çox rəqəm gəlibsə, bu SMS-dən avtomatik doldurmadır (telefon bütün kodu birinci
    // xanaya qoyur) — rəqəmləri xanalara paylayırıq. İstisna: xanada rəqəm var idi və yanına bir rəqəm
    // də yazıldı (bəzi Android klaviaturaları belə göndərir) — onda sadəcə yeni rəqəmi saxlayırıq.
    const typedOverExisting = previous !== '' && typed.length === 2
    if (typed.length > 1 && !typedOverExisting) {
      const pasted = applyPaste(typed, length)
      if (pasted) {
        onChange(pasted.next)
        focusBox(pasted.focus)
      }
      return
    }

    // Rəqəmlər ardıcıl yazılır: boş xanaları atlayıb irəlidəki xanaya yazılsa, rəqəmi ilk boş xanaya
    // qoyuruq. Yoxsa "_ _ 5 _" kimi boşluqlu kod alınar və rəqəmlər sürüşərdi.
    const targetIndex = Math.min(index, value.length)
    const result = applyDigit(value, length, targetIndex, event.target.value)
    onChange(result.next)
    if (result.focus !== index) focusBox(result.focus)
  }

  function handleKeyDown(index: number, event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'Backspace') {
      const result = applyBackspace(value, length, index)
      if (result.handled) {
        event.preventDefault()
        onChange(result.next)
        focusBox(result.focus)
      }
    }
    if (event.key === 'ArrowLeft') focusBox(index - 1)
    if (event.key === 'ArrowRight') focusBox(index + 1)
  }

  function handlePaste(event: ClipboardEvent<HTMLInputElement>) {
    // Yapışdırılan mətndən rəqəm olmayanlar silinir, qalanı xanalara paylanır.
    const result = applyPaste(event.clipboardData.getData('text'), length)
    if (!result) return
    event.preventDefault()
    onChange(result.next)
    focusBox(result.focus)
  }

  return (
    <div
      role="group"
      aria-label={label}
      aria-describedby={hasError && errorId ? errorId : undefined}
      style={{ display: 'flex', gap: 8 }}
    >
      {digits.map((digit, index) => (
        <input
          key={index}
          id={boxId(id, index)}
          ref={(element) => {
            boxes.current[index] = element
          }}
          className="inp"
          type={masked ? 'password' : 'text'}
          inputMode="numeric"
          pattern="[0-9]*"
          autoComplete={index === 0 ? autoComplete : 'off'}
          aria-label={`${label}, ${index + 1}-${ORDINAL_SUFFIX[index + 1] ?? 'ci'} rəqəm`}
          aria-invalid={hasError ? true : undefined}
          value={digit}
          onChange={(event) => handleChange(index, event)}
          onKeyDown={(event) => handleKeyDown(index, event)}
          onPaste={handlePaste}
          // Xanaya girəndə içindəki rəqəm seçilir ki, yeni rəqəm onu əvəz etsin.
          onFocus={(event) => event.currentTarget.select()}
          style={{ textAlign: 'center', fontSize: 22, fontWeight: 700, padding: '10px 0', flex: 1, minWidth: 0 }}
        />
      ))}
    </div>
  )
}
