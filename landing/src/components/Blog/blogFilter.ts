// Bloq siyahısının canlı axtarışı və kateqoriya filtri. BlogIndex.astro ilə bu `data-*` atributları vasitəsilə bağlıdır:
// [data-blog-index] (data-tpl-* şablonları), [data-cat-tab] (data-cat), [data-item] (data-cat, data-kind, data-search).
// Gizlətmə yalnız sarğının `hidden` atributu ilə olur: Preflight yoxdur, sarğıda isə display sinfi yoxdur.
import { matchesQuery, normalizeSearch, splitQuery } from '../../data/blogSearch'

const ALL_CATEGORIES = 'all'
const STATUS_SEPARATOR = ' · '

const root = document.querySelector<HTMLElement>('[data-blog-index]')

if (root) {
  const tabs = Array.from(root.querySelectorAll<HTMLButtonElement>('[data-cat-tab]'))
  const items = Array.from(root.querySelectorAll<HTMLElement>('[data-item]'))
  const input = root.querySelector<HTMLInputElement>('[data-search-input]')
  const clearWrap = root.querySelector<HTMLElement>('[data-search-clear-wrap]')
  const clearButton = clearWrap?.querySelector<HTMLButtonElement>('button')
  const status = root.querySelector<HTMLElement>('[data-search-status]')
  const announce = root.querySelector<HTMLElement>('[data-search-announce]')
  const empty = root.querySelector<HTMLElement>('[data-empty]')
  const emptyTitle = root.querySelector<HTMLElement>('[data-empty-title]')
  const emptyReset = root.querySelector<HTMLElement>('[data-empty-reset]')
  const emptyWidenWrap = root.querySelector<HTMLElement>('[data-empty-widen-wrap]')
  const emptyWiden = root.querySelector<HTMLElement>('[data-empty-widen]')

  // Şablonlar ({n}, {q} yer tutucuları) ui.ts-dən səhifənin dilində gəlir.
  const templateResults = root.dataset.tplResults ?? ''
  const templateQuery = root.dataset.tplQuery ?? ''
  const templateEmptyTitle = root.dataset.tplEmptyTitle ?? ''

  const haystacks = new Map<HTMLElement, string>()
  for (const item of items) {
    haystacks.set(item, normalizeSearch(item.dataset.search ?? ''))
  }

  let activeCategory = ALL_CATEGORIES
  let query = ''

  // Nəticə yalnız textContent ilə yazılır (innerHTML yox): sorğunu istifadəçi yazır.
  const fill = (template: string, values: Record<string, string | number>): string =>
    template.replace(/\{(\w+)\}/g, (placeholder, name: string) =>
      name in values ? String(values[name]) : placeholder,
    )

  const isVisible = (item: HTMLElement, tokens: string[]): boolean => {
    const categoryOk = activeCategory === ALL_CATEGORIES || item.dataset.cat === activeCategory
    const textOk = matchesQuery(haystacks.get(item) ?? '', tokens)
    const searching = tokens.length > 0
    switch (item.dataset.kind) {
      case 'featured':
        return !searching && categoryOk
      case 'featured-copy':
        // Seçilmiş məqalənin şəbəkədəki surəti yalnız axtarışda görünür.
        return searching && categoryOk && textOk
      default:
        return categoryOk && textOk
    }
  }

  const activeTabLabel = (): string => {
    const tab = tabs.find((candidate) => candidate.dataset.cat === activeCategory)
    return tab?.textContent?.trim() ?? ''
  }

  const apply = (): void => {
    const tokens = splitQuery(query)
    const searching = tokens.length > 0
    let visibleCount = 0

    for (const item of items) {
      const visible = isVisible(item, tokens)
      item.hidden = !visible
      if (visible) visibleCount += 1
    }

    for (const tab of tabs) {
      tab.setAttribute('aria-pressed', String(tab.dataset.cat === activeCategory))
    }

    if (clearWrap) clearWrap.hidden = query === ''

    if (status) {
      if (searching && visibleCount > 0) {
        const parts = [fill(templateResults, { n: visibleCount })]
        if (activeCategory !== ALL_CATEGORIES) parts.push(activeTabLabel())
        parts.push(fill(templateQuery, { q: query.trim() }))
        status.textContent = parts.join(STATUS_SEPARATOR)
      } else {
        status.textContent = ''
      }
    }

    const nothingFound = visibleCount === 0
    if (empty) empty.hidden = !nothingFound
    if (emptyTitle && nothingFound) {
      emptyTitle.textContent = fill(templateEmptyTitle, { q: query.trim() })
    }
    if (announce) announce.textContent = nothingFound ? (emptyTitle?.textContent ?? '') : ''
    if (emptyWidenWrap) emptyWidenWrap.hidden = activeCategory === ALL_CATEGORIES
  }

  const setQuery = (value: string): void => {
    query = value
    if (input && input.value !== value) input.value = value
    apply()
  }

  const clearSearch = (): void => {
    setQuery('')
    input?.focus()
  }

  for (const tab of tabs) {
    tab.addEventListener('click', () => {
      activeCategory = tab.dataset.cat ?? ALL_CATEGORIES
      apply()
    })
  }

  input?.addEventListener('input', () => setQuery(input.value))
  input?.addEventListener('keydown', (event) => {
    if (event.key === 'Escape' && query !== '') {
      event.preventDefault()
      clearSearch()
    }
  })
  clearButton?.addEventListener('click', clearSearch)
  emptyReset?.addEventListener('click', clearSearch)
  emptyWiden?.addEventListener('click', () => {
    activeCategory = ALL_CATEGORIES
    apply()
  })

  // Geri düyməsi ilə qayıdanda brauzer input mətnini bərpa edə bilər: filtri onunla eyniləşdiririk.
  query = input?.value ?? ''
  apply()
}
