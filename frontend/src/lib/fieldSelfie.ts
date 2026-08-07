/**
 * Compress a captured camera photo (a File from an <input type="file" capture="user">) to a small WebP
 * data URL, so a field-visit selfie uploads as ~50–150 KB instead of a multi-MB phone photo. Mirrors
 * what the scan sends the server. Returns null if the file can't be decoded.
 */
export async function fileToWebp(file: File, maxDim = 900, quality = 0.72): Promise<string | null> {
  const url = URL.createObjectURL(file)
  try {
    const img = await new Promise<HTMLImageElement>((resolve, reject) => {
      const i = new Image()
      i.onload = () => resolve(i)
      i.onerror = reject
      i.src = url
    })
    const scale = Math.min(1, maxDim / Math.max(img.width, img.height))
    const w = Math.max(1, Math.round(img.width * scale))
    const h = Math.max(1, Math.round(img.height * scale))
    const canvas = document.createElement('canvas')
    canvas.width = w
    canvas.height = h
    const ctx = canvas.getContext('2d')
    if (!ctx) return null
    ctx.drawImage(img, 0, 0, w, h)
    return canvas.toDataURL('image/webp', quality)
  } catch {
    return null
  } finally {
    URL.revokeObjectURL(url)
  }
}
