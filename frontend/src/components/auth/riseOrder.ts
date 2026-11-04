import type { CSSProperties } from 'react'

/* Opening order */
export function riseOrder(order: number): CSSProperties {
  return { '--i': order } as CSSProperties
}
