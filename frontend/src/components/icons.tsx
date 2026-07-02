import type { SVGProps } from 'react'

const base: SVGProps<SVGSVGElement> = {
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 2,
  strokeLinecap: 'round',
  strokeLinejoin: 'round',
}

export const IconClipboard = (p: SVGProps<SVGSVGElement>) => (
  <svg {...base} {...p}>
    <rect x="8" y="2" width="8" height="4" rx="1" />
    <path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2" />
    <line x1="12" y1="11" x2="12" y2="17" />
    <line x1="9" y1="14" x2="15" y2="14" />
  </svg>
)

export const IconChart = (p: SVGProps<SVGSVGElement>) => (
  <svg {...base} {...p}>
    <path d="M3 3v18h18" />
    <path d="M7 16l4-4 4 4 4-8" />
  </svg>
)

export const IconSend = (p: SVGProps<SVGSVGElement>) => (
  <svg {...base} {...p}>
    <line x1="22" y1="2" x2="11" y2="13" />
    <polygon points="22 2 15 22 11 13 2 9 22 2" />
  </svg>
)

export const IconPhone = (p: SVGProps<SVGSVGElement>) => (
  <svg {...base} {...p}>
    <rect x="7" y="2" width="10" height="20" rx="2" />
    <circle cx="12" cy="18" r="1" fill="currentColor" stroke="none" />
  </svg>
)

export const IconDownload = (p: SVGProps<SVGSVGElement>) => (
  <svg {...base} {...p}>
    <path d="M12 3v12" />
    <polyline points="7 11 12 16 17 11" />
    <path d="M5 19h14" />
  </svg>
)

export const IconLogout = (p: SVGProps<SVGSVGElement>) => (
  <svg {...base} {...p}>
    <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
    <polyline points="16 17 21 12 16 7" />
    <line x1="21" y1="12" x2="9" y2="12" />
  </svg>
)

export const IconCheck = (p: SVGProps<SVGSVGElement>) => (
  <svg {...base} strokeWidth={2.5} {...p}>
    <polyline points="4 12 9 17 20 6" />
  </svg>
)

export const IconX = (p: SVGProps<SVGSVGElement>) => (
  <svg {...base} strokeWidth={2.5} {...p}>
    <line x1="5" y1="5" x2="19" y2="19" />
    <line x1="19" y1="5" x2="5" y2="19" />
  </svg>
)

export const IconClock = (p: SVGProps<SVGSVGElement>) => (
  <svg {...base} {...p}>
    <circle cx="12" cy="12" r="9" />
    <polyline points="12 7 12 12 16 14" />
  </svg>
)
