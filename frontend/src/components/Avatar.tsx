import { initials } from '../lib/att'
import { cachedAvatar } from '../lib/avatar'

/**
 * Somebody's face, or their initials when there is no picture.
 *
 * The picture comes from the local cache, never from the network — the one screen that most needs
 * faces is a crew phone standing where there is no signal, and an <img> pointed at a presigned URL
 * would render thirty broken frames there. Whoever puts an avatar INTO the cache does it once (see
 * lib/avatar.ts); this only reads.
 */
export function Avatar({
  employeeId,
  name,
  size,
  className = '',
  /** Overrides the cache — used while a freshly chosen picture is still uploading. */
  dataUrl,
}: {
  employeeId: string | null | undefined
  name: string | null | undefined
  size: number
  className?: string
  dataUrl?: string | null
}) {
  const src = dataUrl ?? cachedAvatar(employeeId)

  if (src) {
    return (
      <img
        src={src}
        alt=""
        width={size}
        height={size}
        className={`shrink-0 rounded-full object-cover ${className}`}
        style={{ width: size, height: size }}
      />
    )
  }

  return (
    <span
      className={`flex shrink-0 items-center justify-center rounded-full bg-blue-100 font-bold text-blue-700 ${className}`}
      style={{ width: size, height: size, fontSize: Math.round(size * 0.34) }}
    >
      {initials(name)}
    </span>
  )
}
