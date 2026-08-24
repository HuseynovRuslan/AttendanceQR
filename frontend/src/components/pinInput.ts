/**
 * The decisions behind the four-box PIN field, kept out of the component so they can be tested — the
 * project has no component-rendering stack, and these are the parts that would strand somebody anyway:
 * a pasted temporary PIN landing in one box, or backspace not walking back.
 */

/** One box took a keystroke. Returns the whole PIN, and where the caret should go next. */
export function applyDigit(value: string, length: number, index: number, raw: string): {
  next: string
  focus: number
} {
  const digit = raw.replace(/\D/g, '').slice(-1)
  const chars = Array.from({ length }, (_, i) => value[i] ?? '')
  chars[index] = digit
  return {
    next: chars.join('').slice(0, length),
    // Advance only on entry: deleting should leave the caret on the box being corrected rather than
    // jumping away from it.
    focus: digit ? Math.min(index + 1, length - 1) : index,
  }
}

/** Backspace. On an empty box it clears the previous one and steps back, which is what everyone expects. */
export function applyBackspace(value: string, length: number, index: number): {
  next: string
  focus: number
  handled: boolean
} {
  const chars = Array.from({ length }, (_, i) => value[i] ?? '')
  if (chars[index] || index === 0) return { next: value, focus: index, handled: false }
  chars[index - 1] = ''
  return { next: chars.join(''), focus: index - 1, handled: true }
}

/**
 * A pasted value. Temporary PINs arrive by message and get pasted with whatever punctuation came with
 * them, so everything that is not a digit is dropped and the rest fills the row.
 */
export function applyPaste(pasted: string, length: number): { next: string; focus: number } | null {
  const digits = pasted.replace(/\D/g, '').slice(0, length)
  if (!digits) return null
  return { next: digits, focus: Math.min(digits.length, length - 1) }
}
