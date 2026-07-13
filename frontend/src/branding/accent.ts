/**
 * Per-tenant accent colour. The whole design system is driven by the `--leaf` family (see theme.css);
 * a tenant just supplies ONE accent hex and we derive the darker/hover shade, a pale background tint,
 * and a readable text-on-accent colour from it — so buttons, active nav, focus rings and badges all
 * recolour at once. bax (no colour) keeps the built-in green defaults untouched.
 */

interface Rgb {
  r: number
  g: number
  b: number
}

function parseHex(hex: string): Rgb | null {
  const m = /^#?([0-9a-f]{6})$/i.exec(hex.trim())
  if (!m) return null
  const n = parseInt(m[1], 16)
  return { r: (n >> 16) & 255, g: (n >> 8) & 255, b: n & 255 }
}

const clamp = (v: number) => Math.max(0, Math.min(255, Math.round(v)))
const toHex = ({ r, g, b }: Rgb) =>
  '#' + [r, g, b].map((v) => clamp(v).toString(16).padStart(2, '0')).join('')

/** Perceived brightness 0..1 — decides whether text on the accent should be dark or white. */
function brightness({ r, g, b }: Rgb) {
  return (0.299 * r + 0.587 * g + 0.114 * b) / 255
}

export interface AccentPalette {
  base: string
  dark: string
  bg: string
  on: string
}

export function deriveAccent(hex: string): AccentPalette | null {
  const rgb = parseHex(hex)
  if (!rgb) return null
  // hover/darker: pull each channel ~18% toward black
  const dark = toHex({ r: rgb.r * 0.82, g: rgb.g * 0.82, b: rgb.b * 0.82 })
  // pale tint for badge/feedback backgrounds: 14% accent over white
  const mix = (c: number) => c * 0.14 + 255 * 0.86
  const bg = toHex({ r: mix(rgb.r), g: mix(rgb.g), b: mix(rgb.b) })
  // readable text sitting ON the accent (e.g. a gold button needs dark text, a navy one needs white)
  const on = brightness(rgb) > 0.55 ? '#17233b' : '#ffffff'
  return { base: toHex(rgb), dark, bg, on }
}

/** Paint the derived palette onto :root so every `--leaf`-based rule adopts the tenant's colour. */
export function applyAccent(hex: string) {
  const p = deriveAccent(hex)
  if (!p) return
  const root = document.documentElement.style
  root.setProperty('--leaf', p.base)
  root.setProperty('--leaf-d', p.dark)
  root.setProperty('--leaf-bg', p.bg)
  root.setProperty('--on-leaf', p.on)
}
