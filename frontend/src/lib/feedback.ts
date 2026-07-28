// Haptic + audio confirmation for a finished scan. The point is that an employee knows the tap
// "took" WITHOUT reading the screen — a gardener in bright morning sun often can't read a phone at
// all. Not knowing is exactly what makes people scan a second time and land on "çox tez çıxış"
// (TooSoonToCheckOut). A short buzz + chime ends that guessing.
//
// Everything here is best-effort and never throws: vibration is Android-only (iOS blocks it), and
// audio needs an unlocked AudioContext. If neither is available the visual card still says it all.

let audioCtx: AudioContext | null = null

function ctx(): AudioContext | null {
  try {
    if (!audioCtx) {
      const AC = window.AudioContext || (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
      if (!AC) return null
      audioCtx = new AC()
    }
    // A context created outside a gesture starts "suspended"; resume() inside/after one unlocks it.
    if (audioCtx.state === 'suspended') void audioCtx.resume()
    return audioCtx
  } catch {
    return null
  }
}

// Prime the audio context on the first touch anywhere on the page, so the success chime can play
// later even when the scan itself (a QR decode) is not a user gesture. Vibration needs no priming.
export function primeFeedbackOnGesture(): void {
  const prime = () => {
    ctx()
  }
  window.addEventListener('pointerdown', prime, { once: true, passive: true })
  window.addEventListener('touchstart', prime, { once: true, passive: true })
}

function vibrate(pattern: number | number[]): void {
  try {
    navigator.vibrate?.(pattern)
  } catch {
    /* unsupported (iOS) — the chime and the card carry it */
  }
}

// A single synthesised note — no audio asset to ship, and it can't fail to load on a weak connection.
function tone(freq: number, startMs: number, durMs: number): void {
  const ac = ctx()
  if (!ac) return
  try {
    const osc = ac.createOscillator()
    const gain = ac.createGain()
    osc.type = 'sine'
    osc.frequency.value = freq
    const t0 = ac.currentTime + startMs / 1000
    const t1 = t0 + durMs / 1000
    // Quick fade in/out so it's a soft chime, not a click. exponentialRamp can't target 0.
    gain.gain.setValueAtTime(0.0001, t0)
    gain.gain.exponentialRampToValueAtTime(0.16, t0 + 0.012)
    gain.gain.exponentialRampToValueAtTime(0.0001, t1)
    osc.connect(gain).connect(ac.destination)
    osc.start(t0)
    osc.stop(t1 + 0.02)
  } catch {
    /* audio unavailable — ignore */
  }
}

/** A completed check-in / check-out: a short double buzz + two rising notes ("ta-da"). */
export function successFeedback(): void {
  vibrate([45, 40, 45])
  tone(660, 0, 110)
  tone(880, 120, 150)
}

/** A rejected scan (wrong device, network dead): one longer buzz + a low note — deliberately
 *  different from success so the difference is felt, not read. */
export function errorFeedback(): void {
  vibrate(200)
  tone(220, 0, 240)
}
