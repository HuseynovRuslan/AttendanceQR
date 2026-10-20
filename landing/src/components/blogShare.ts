// «Linki kopyala» düyməsi (BlogPost.astro). HTML ilə `data-*` atributları ilə bağlıdır:
//   [data-copy-link]    düymə; data-url — kopyalanacaq ünvan, data-label-copied — uğur mətni
//   [data-copy-label]   düymənin içindəki yazı
//   [data-copy-status]  ekran oxuyucu üçün gizli sətir (aria-live)

// Uğur mətni bu qədər (ms) görünüb əvvəlki hala qayıdır.
const COPIED_RESET_MS = 1800

const button = document.querySelector<HTMLButtonElement>('[data-copy-link]')
const label = button?.querySelector<HTMLElement>('[data-copy-label]')
const statusLine = document.querySelector<HTMLElement>('[data-copy-status]')

// Köhnə brauzerlər və ya icazə verilməyən hallar üçün ehtiyat: müvəqqəti <textarea> + execCommand.
// Kopyalanıb-kopyalanmadığını (true/false) qaytarır; fokus düyməyə qaytarılır ki, klaviatura istifadəçisi yerini itirməsin.
function copyWithTextarea(text: string): boolean {
  const previouslyFocused = document.activeElement as HTMLElement | null
  const area = document.createElement('textarea')
  area.value = text
  area.setAttribute('readonly', '')
  area.style.position = 'fixed'
  area.style.opacity = '0'
  document.body.appendChild(area)
  area.select()
  let copied = false
  try {
    copied = document.execCommand('copy')
  } catch {
    copied = false
  }
  area.remove()
  previouslyFocused?.focus()
  return copied
}

async function copyText(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text)
    return true
  } catch {
    return copyWithTextarea(text)
  }
}

if (button && label) {
  const originalLabel = label.textContent ?? ''
  const copiedLabel = button.dataset.labelCopied ?? originalLabel
  let resetTimer: number | undefined

  button.addEventListener('click', async () => {
    const copied = await copyText(button.dataset.url ?? window.location.href)
    // Kopyalama alınmayıbsa uğur mətni göstərilmir.
    if (!copied) return

    button.dataset.copied = 'true'
    label.textContent = copiedLabel
    if (statusLine) statusLine.textContent = copiedLabel

    window.clearTimeout(resetTimer)
    resetTimer = window.setTimeout(() => {
      delete button.dataset.copied
      label.textContent = originalLabel
      if (statusLine) statusLine.textContent = ''
    }, COPIED_RESET_MS)
  })
}
