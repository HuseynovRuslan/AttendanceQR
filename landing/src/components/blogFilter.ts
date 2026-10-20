// Bloq siyahısının canlı axtarışı və kateqoriya filtri (brauzerdə işləyir).
// HTML ilə bu fayl `data-*` atributları ilə bağlıdır (BlogIndex.astro-da eyni adlar var):
//   [data-blog-index]   kök element; data-tpl-* atributları ui.ts-dən gələn mətn şablonlarıdır
//   [data-cat-tab]      kateqoriya düyməsi (data-cat), aktivliyi aria-pressed göstərir
//   [data-item]         filtrlənən sarğı: data-cat, data-kind (featured | featured-copy | post), data-search
// Elementi gizlətmək üçün yalnız sarğıdakı `hidden` atributu dəyişir: Preflight yoxdur, ona görə `display`
// sinfi olan elementdə `hidden` işləməzdi — sarğıların özündə display sinfi yoxdur.
import { matchesQuery, normalizeSearch, splitQuery } from '../data/blogSearch'

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
  // Ekran oxuyucu üçün gizli sətir: nəticə tapılmayanda boş vəziyyət başlığını oxudur.
  const announce = root.querySelector<HTMLElement>('[data-search-announce]')
  const empty = root.querySelector<HTMLElement>('[data-empty]')
  const emptyTitle = root.querySelector<HTMLElement>('[data-empty-title]')
  const emptyReset = root.querySelector<HTMLElement>('[data-empty-reset]')
  const emptyWidenWrap = root.querySelector<HTMLElement>('[data-empty-widen-wrap]')
  const emptyWiden = root.querySelector<HTMLElement>('[data-empty-widen]')

  // Mətn şablonları ({n} və {q} yer tutucuları) ui.ts-dən, həmin səhifənin dilində gəlir.
  const templateResults = root.dataset.tplResults ?? ''
  const templateQuery = root.dataset.tplQuery ?? ''
  const templateEmptyTitle = root.dataset.tplEmptyTitle ?? ''

  // Hər məqalənin axtarış mətni başlanğıcda bir dəfə normallaşdırılır (hər düyməyə basanda yox).
  const haystacks = new Map<HTMLElement, string>()
  for (const item of items) {
    haystacks.set(item, normalizeSearch(item.dataset.search ?? ''))
  }

  // Vəziyyət: seçilmiş kateqoriya və axtarış mətni. Hər dəyişiklikdə apply() hər şeyi yenidən hesablayır.
  let activeCategory = ALL_CATEGORIES
  let query = ''

  // '{n}' kimi yer tutucuları dəyərlə əvəz edir. Nəticə yalnız textContent ilə yazılır (innerHTML yox),
  // çünki sorğunu istifadəçi yazır.
  const fill = (template: string, values: Record<string, string | number>): string =>
    template.replace(/\{(\w+)\}/g, (placeholder, name: string) =>
      name in values ? String(values[name]) : placeholder,
    )

  // Bir məqalə (və ya böyük kart) hazırkı filtrdə görünməlidirmi?
  const isVisible = (item: HTMLElement, tokens: string[]): boolean => {
    const categoryOk = activeCategory === ALL_CATEGORIES || item.dataset.cat === activeCategory
    const textOk = matchesQuery(haystacks.get(item) ?? '', tokens)
    const searching = tokens.length > 0
    switch (item.dataset.kind) {
      case 'featured':
        // Böyük kart yalnız axtarış yoxdursa görünür.
        return !searching && categoryOk
      case 'featured-copy':
        // Seçilmiş məqalənin şəbəkədəki gizli surəti yalnız axtarışda, nəticələrin arasında çıxır.
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

    // Status sətri: yalnız axtarış aktiv və nəticə varsa dolur.
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

    // Boş vəziyyət: heç bir məqalə görünmürsə.
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

  // Geri düyməsi ilə qayıdanda brauzer input mətnini bərpa edə bilər, filtr isə sıfırlanıb: eyniləşdiririk.
  query = input?.value ?? ''
  apply()
}
