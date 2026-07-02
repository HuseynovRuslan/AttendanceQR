/** Bakı Abadlıq leaf mark. */
export function BrandLogo({ size = 34 }: { size?: number }) {
  return (
    <svg viewBox="0 0 100 120" fill="none" style={{ width: size, height: size }}>
      <path d="M50 4C74 30 92 55 92 76C92 100 73 116 50 116C27 116 8 100 8 76C8 55 26 30 50 4Z" fill="#3D3E3E" />
      <path d="M50 20C68 40 82 58 82 74C82 92 68 104 50 104C32 104 18 92 18 74C18 58 32 40 50 20Z" fill="#F7F6F2" />
      <path d="M50 32C63 47 73 60 73 72C73 85 62 94 50 94C38 94 27 85 27 72C27 60 37 47 50 32Z" fill="#7CB342" />
      <path d="M50 40V88M50 66L38 56M50 70L64 58" stroke="#F7F6F2" strokeWidth="3" strokeLinecap="round" />
    </svg>
  )
}
