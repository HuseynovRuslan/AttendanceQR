import { useEffect, useRef, useState, type CSSProperties } from 'react'
import loginScene from '../../assets/login/login-scene-wide.webp'
import registerScene from '../../assets/login/register-scene-wide.webp'
import registerPoster from '../../assets/login/register-poster.webp'
import codeScene from '../../assets/login/code-scene-wide.webp'
import pinScene from '../../assets/login/pin-scene-wide.webp'
import './AuthScene.css'

export type AuthSceneVariant = 'login' | 'register' | 'code' | 'pin'

/* Images */
const SCENE_IMAGES: Record<AuthSceneVariant, string> = {
  login: loginScene,
  register: registerScene,
  code: codeScene,
  pin: pinScene,
}

/* Ring centres */
const RING_CENTERS: Record<AuthSceneVariant, { x: number; y: number }> = {
  login: { x: 500.5, y: 440.5 },
  register: { x: 499, y: 405 },
  code: { x: 530, y: 540 },
  pin: { x: 273, y: 416 },
}

/* Error mark: where the red corners and ✕ go */
const ALERT_MARKS: Record<AuthSceneVariant, { x: number; y: number; r: number; corners?: string }> = {
  login: { x: 500.5, y: 440.5, r: 17, corners: 'M474 424v-10h10M517 414h10v10M527 457v10h-10M484 467h-10v-10' },
  register: { x: 500.5, y: 440.5, r: 17, corners: 'M474 424v-10h10M517 414h10v10M527 457v10h-10M484 467h-10v-10' },
  code: { x: 486, y: 502, r: 15 },
  pin: { x: 332, y: 342, r: 17, corners: 'M214 362v-20h20M312 342h20v20M332 470v20h-20M234 490h-20v-20' },
}
const ALERT_MS = 1400

const CODE_DIGITS = ['4', '8', '2', '9', '1', '6']

type AuthSceneProps = {
  variant: AuthSceneVariant
  className?: string
  alertKey?: number
}

export function AuthScene({ variant, className = '', alertKey = 0 }: AuthSceneProps) {
  /* Scene swap */
  const firstVariant = useRef<AuthSceneVariant | null>(variant)
  if (variant !== firstVariant.current) firstVariant.current = null
  const stageClass = firstVariant.current === null ? 'auth-scene__stage auth-scene__stage--swap' : 'auth-scene__stage'

  /* One-shot error */
  const [alerting, setAlerting] = useState(false)
  useEffect(() => {
    if (alertKey === 0) return
    setAlerting(true)
    const timerId = window.setTimeout(() => setAlerting(false), ALERT_MS)
    return () => window.clearTimeout(timerId)
  }, [alertKey])

  const sceneClass = alerting ? 'auth-scene auth-scene--alert' : 'auth-scene'
  const frameStyle = { '--scene-image': `url(${SCENE_IMAGES[variant]})` } as CSSProperties

  return (
    <div className={`auth-scene-frame ${className}`} style={frameStyle} aria-hidden="true">
      <div className={sceneClass}>
        <div className="auth-scene__parallax">
          <div key={variant} className={stageClass}>
            <img className="auth-scene__img" src={SCENE_IMAGES[variant]} alt="" draggable={false} />

            <svg
              className="auth-scene__overlay"
              viewBox="-227 0 1350 1200"
              preserveAspectRatio="xMidYMid slice"
              fill="none"
              strokeLinecap="round"
              strokeLinejoin="round"
            >
              <OpeningRings x={RING_CENTERS[variant].x} y={RING_CENTERS[variant].y} />
              <g className="as-layer">
                {variant === 'login' && <LoginOverlay />}
                {variant === 'register' && <RegisterOverlay />}
                {variant === 'code' && <CodeOverlay />}
                {variant === 'pin' && <PinOverlay />}
              </g>
              {alerting && <AlertMark key={alertKey} {...ALERT_MARKS[variant]} />}
            </svg>
          </div>
        </div>
      </div>
    </div>
  )
}

/* Error mark */
function AlertMark({ x, y, r, corners }: { x: number; y: number; r: number; corners?: string }) {
  const arm = r * 0.36
  return (
    <g>
      {corners && <path className="as-alert-corners" d={corners} stroke="#D92D20" strokeWidth="3" />}
      <g className="as-alert-x as-pop">
        <circle cx={x} cy={y} r={r} fill="#D92D20" stroke="#fff" strokeWidth="3" />
        <path
          d={`M${x - arm} ${y - arm}L${x + arm} ${y + arm}M${x + arm} ${y - arm}L${x - arm} ${y + arm}`}
          stroke="#fff"
          strokeWidth="3.6"
        />
      </g>
    </g>
  )
}

/* Lamp glow */
function GlowGradient({ id }: { id: string }) {
  return (
    <radialGradient id={id} cx=".5" cy=".15" r=".85">
      <stop offset="0" stopColor="#FFF7DC" stopOpacity=".7" />
      <stop offset=".45" stopColor="#FFF7DC" stopOpacity=".18" />
      <stop offset="1" stopColor="#FFF7DC" stopOpacity="0" />
    </radialGradient>
  )
}

/* Opening rings */
function OpeningRings({ x, y }: { x: number; y: number }) {
  return (
    <g stroke="#1560FF">
      <circle className="as-ping" style={{ animationDelay: '1s' }} cx={x} cy={y} r="260" strokeWidth="2" vectorEffect="non-scaling-stroke" />
      <circle className="as-ping" style={{ animationDelay: '1.15s' }} cx={x} cy={y} r="260" strokeWidth="1.5" vectorEffect="non-scaling-stroke" />
      <circle className="as-ping" style={{ animationDelay: '1.3s' }} cx={x} cy={y} r="260" strokeWidth="1" vectorEffect="non-scaling-stroke" />
    </g>
  )
}

/* Login overlay */
function LoginOverlay() {
  return (
    <>
      <defs>
        <GlowGradient id="glow-login" />
        <clipPath id="login-qr-clip">
          <rect x="474" y="414" width="53" height="53" />
        </clipPath>
      </defs>

      {/* Lamp */}
      <g className="as-loop as-lamp">
        <ellipse cx="645" cy="262" rx="105" ry="70" fill="url(#glow-login)" />
        <circle cx="645" cy="209" r="9" fill="#FFF3C4" />
      </g>

      {/* Scanner corners */}
      <g className="as-loop as-brackets" stroke="#1560FF" strokeWidth="3">
        <path d="M474 424v-10h10M517 414h10v10M527 457v10h-10M484 467h-10v-10" />
      </g>

      {/* Scan line */}
      <g clipPath="url(#login-qr-clip)">
        <g className="as-loop as-sweep">
          <rect x="474" y="417" width="53" height="3" rx="1.5" fill="#1560FF" />
          <rect x="474" y="405" width="53" height="12" fill="#1560FF" opacity=".12" />
        </g>
      </g>

      {/* Check */}
      <g className="as-loop as-check as-pop">
        <circle cx="500.5" cy="440.5" r="17" fill="#1560FF" stroke="#fff" strokeWidth="3" />
        <path d="M492 441l6 6 11-12" stroke="#fff" strokeWidth="3.6" />
      </g>

      {/* Monitor */}
      <g className="as-loop as-screen">
        <rect x="251" y="586" width="77" height="68" fill="#FFFFFF" />
        <rect x="251" y="586" width="77" height="10" fill="#1560FF" />
        <rect x="257" y="603" width="34" height="5" rx="2.5" fill="#C9D6EE" />
        <circle cx="262" cy="622" r="5.5" fill="#E6F0FF" />
        <rect x="271" y="619" width="26" height="5" rx="2.5" fill="#C9D6EE" />
        <rect x="302" y="617" width="20" height="10" rx="5" fill="#DDF5E5" />
        <path d="M307.5 622l2.5 2.5 5-5" stroke="#1E8E4E" strokeWidth="1.8" />
        <circle cx="262" cy="640" r="5.5" fill="#E6F0FF" />
        <rect x="271" y="637" width="22" height="5" rx="2.5" fill="#C9D6EE" />
        <rect x="302" y="635" width="20" height="10" rx="5" fill="#DDF5E5" />
        <path d="M307.5 640l2.5 2.5 5-5" stroke="#1E8E4E" strokeWidth="1.8" />
      </g>
    </>
  )
}

/* Register overlay */
function RegisterOverlay() {
  return (
    <>
      <defs>
        <GlowGradient id="glow-register" />
        <clipPath id="reg-reveal">
          <rect className="as-loop as-reveal" x="437" y="309" width="125" height="193" />
        </clipPath>
      </defs>

      {/* Lamp */}
      <g className="as-loop as-lamp-reg">
        <ellipse cx="645" cy="262" rx="105" ry="70" fill="url(#glow-register)" />
        <circle cx="645" cy="209" r="9" fill="#FFF3C4" />
      </g>

      {/* Poster */}
      <g className="as-loop as-posterfade">
        <image href={registerPoster} x="437" y="309" width="125" height="193" clipPath="url(#reg-reveal)" />
      </g>

      {/* Print line */}
      <g className="as-loop as-printline">
        <rect x="437" y="308" width="125" height="3" rx="1.5" fill="#1560FF" />
        <rect x="437" y="296" width="125" height="12" fill="#1560FF" opacity=".12" />
      </g>

      {/* Check */}
      <g className="as-loop as-check-reg as-pop">
        <circle cx="500.5" cy="440.5" r="17" fill="#1560FF" stroke="#fff" strokeWidth="3" />
        <path d="M492 441l6 6 11-12" stroke="#fff" strokeWidth="3.6" />
      </g>

      {/* Monitor */}
      <g className="as-loop as-screen-reg">
        <rect x="251" y="586" width="77" height="68" fill="#FFFFFF" />
        <rect x="251" y="586" width="77" height="10" fill="#1560FF" />
        <rect x="257" y="603" width="40" height="5" rx="2.5" fill="#C9D6EE" />
        <rect x="257" y="616" width="64" height="16" rx="4" fill="#E6F0FF" />
        <circle cx="266" cy="624" r="4" fill="#1560FF" />
        <rect x="274" y="621" width="30" height="5" rx="2.5" fill="#9DBEFF" />
        <rect x="257" y="637" width="40" height="10" rx="5" fill="#DDF5E5" />
        <path d="M262 642l2.5 2.5 5-5" stroke="#1E8E4E" strokeWidth="1.8" />
      </g>
    </>
  )
}

/* Code overlay */
function CodeOverlay() {
  return (
    <>
      <defs>
        <GlowGradient id="glow-code" />
        <filter id="code-shadow" x="-20%" y="-20%" width="140%" height="160%">
          <feDropShadow dx="0" dy="6" stdDeviation="8" floodColor="#0B2A6F" floodOpacity=".18" />
        </filter>
      </defs>

      {/* Lamp */}
      <g className="as-loop as-lamp-code">
        <ellipse cx="645" cy="262" rx="105" ry="70" fill="url(#glow-code)" />
        <circle cx="645" cy="209" r="9" fill="#FFF3C4" />
      </g>

      {/* SMS bubble */}
      <g className="as-loop as-bubble">
        <g filter="url(#code-shadow)">
          <rect x="282" y="500" width="208" height="96" rx="18" fill="#FFFFFF" stroke="#DCE6FA" strokeWidth="1.5" />
          <path d="M488 534l14 8-14 8z" fill="#FFFFFF" />
        </g>
        <rect x="294" y="514" width="20" height="15" rx="4" fill="#1560FF" />
        <path d="M299 529l-2 5 6-5z" fill="#1560FF" />
        <path d="M299 520h10M299 524h6" stroke="#fff" strokeWidth="1.8" />
        <text x="320" y="529" fontFamily="inherit" fontSize="16" fontWeight="700" fill="#52607A">
          QRLog kodu
        </text>
        {CODE_DIGITS.map((digit, index) => (
          <text
            key={index}
            className={`as-loop as-d${index}`}
            x={300 + index * 30}
            y="577"
            fontFamily="inherit"
            fontSize="27"
            fontWeight="800"
            fill="#0B2A6F"
          >
            {digit}
          </text>
        ))}
      </g>

      {/* Check */}
      <g className="as-loop as-okb as-pop">
        <circle cx="486" cy="502" r="15" fill="#1560FF" stroke="#fff" strokeWidth="3" />
        <path d="M479 502l5 5 9-10" stroke="#fff" strokeWidth="3.2" />
      </g>
    </>
  )
}

/* PIN overlay */
function PinOverlay() {
  return (
    <>
      <defs>
        <GlowGradient id="glow-pin" />
        <clipPath id="pin-face">
          <rect x="214" y="342" width="118" height="148" rx="10" />
        </clipPath>
        <filter id="pin-shadow" x="-20%" y="-20%" width="140%" height="160%">
          <feDropShadow dx="0" dy="6" stdDeviation="8" floodColor="#0B2A6F" floodOpacity=".18" />
        </filter>
      </defs>

      {/* Lamp */}
      <g className="as-loop as-flamp">
        <ellipse cx="675" cy="292" rx="105" ry="70" fill="url(#glow-pin)" />
        <circle cx="675" cy="238" r="9" fill="#FFF3C4" />
      </g>

      {/* Face corners */}
      <g className="as-loop as-fbr" stroke="#7FB0FF" strokeWidth="4">
        <path d="M214 362v-20h20M312 342h20v20M332 470v20h-20M234 490h-20v-20" />
      </g>

      {/* Face scan line */}
      <g clipPath="url(#pin-face)">
        <g className="as-loop as-fsw">
          <rect x="214" y="345" width="118" height="3" fill="#7FB0FF" />
          <rect x="214" y="331" width="118" height="14" fill="#7FB0FF" opacity=".18" />
        </g>
      </g>

      {/* Check */}
      <g className="as-loop as-fok as-pop">
        <circle cx="332" cy="342" r="17" fill="#1560FF" stroke="#fff" strokeWidth="3" />
        <path d="M324 342l6 6 11-12" stroke="#fff" strokeWidth="3.6" />
      </g>

      {/* New PIN card */}
      <g className="as-loop as-fpin">
        <g filter="url(#pin-shadow)">
          <rect x="380" y="300" width="176" height="62" rx="16" fill="#fff" stroke="#DCE6FA" strokeWidth="1.5" />
        </g>
        <text x="398" y="326" fontFamily="inherit" fontSize="15" fontWeight="700" fill="#52607A">
          Yeni PIN hazırdır
        </text>
        <g fill="#0B2A6F">
          <circle cx="404" cy="345" r="5" />
          <circle cx="422" cy="345" r="5" />
          <circle cx="440" cy="345" r="5" />
          <circle cx="458" cy="345" r="5" />
        </g>
        <rect x="490" y="335" width="52" height="20" rx="10" fill="#DDF5E5" />
        <path d="M506 345l4 4 8-8" stroke="#1E8E4E" strokeWidth="2.2" />
      </g>
    </>
  )
}
