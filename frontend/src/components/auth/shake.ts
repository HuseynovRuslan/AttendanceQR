/* Shake restart: two identical keyframes, the class alternates on every new error */
export function shakeClass(errorCount: number): string {
  if (errorCount === 0) return ''
  return errorCount % 2 === 1 ? 'auth-shake-a' : 'auth-shake-b'
}
