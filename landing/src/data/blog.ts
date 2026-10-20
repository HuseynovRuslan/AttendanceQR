import { getCollection, type CollectionEntry } from 'astro:content'
import { localizedPath, languages, type Lang, type UIKey } from '../i18n/ui'

// Bloq məqalələri src/content/blog/{az,ru,en}/ qovluqlarındadır. Astro hər məqalənin `id`-sini
// "dil/fayl-adı" kimi verir (məs. 'az/saxta-giris'): aşağıdakı funksiyalar bu id-ni dilə və slug-a ayırır.
// Fayl adı (slug) hər dildə eynidir — dil seçici məhz buna görə həmin məqalənin tərcüməsinə aparır.

export type BlogPost = CollectionEntry<'blog'>

// Məqalənin dili: id-nin ilk hissəsi ('az/saxta-giris' → 'az').
export function postLang(post: BlogPost): Lang {
  return post.id.split('/')[0] as Lang
}

// Məqalənin ünvan hissəsi: dil qovluğundan sonrakı qalan ('az/saxta-giris' → 'saxta-giris').
export function postSlug(post: BlogPost): string {
  return post.id.split('/').slice(1).join('/')
}

// Bloqun ünvanı verilən dildə: blogPath('ru') → '/ru/blog/', blogPath('ru', 'saxta-giris') → '/ru/blog/saxta-giris/'.
export function blogPath(lang: Lang, slug?: string): string {
  return localizedPath(lang, slug ? `/blog/${slug}/` : '/blog/')
}

// Tarixin hansı dilin qaydası ilə yazılacağı (toLocaleDateString-in birinci arqumenti).
export const dateLocale: Record<Lang, string> = { az: 'az-AZ', ru: 'ru-RU', en: 'en-US' }

// Build zamanı yoxlama: hər məqalənin üç dildə qarşılığı eyni slug ilə olmalıdır. Yoxsa
// dil seçici oxucunu mövcud olmayan səhifəyə (404) aparardı. Çatışmayan olsa, build dayanır.
function assertTranslated(posts: BlogPost[]): void {
  const slugsByLang = new Map<Lang, Set<string>>(
    languages.map((lang) => [lang, new Set(posts.filter((p) => postLang(p) === lang).map(postSlug))]),
  )
  // Hər dildəki hər slug bütün dillərdə olmalıdır (yalnız ru və ya en-də olan məqalə də dil seçicini sındırardı).
  const allSlugs = new Set(posts.map(postSlug))
  for (const slug of allSlugs) {
    for (const lang of languages) {
      if (!slugsByLang.get(lang)?.has(slug)) {
        throw new Error(
          `Bloq: "${slug}" məqaləsinin "${lang}" dilində tərcüməsi yoxdur (src/content/blog/${lang}/${slug}.md).`,
        )
      }
    }
  }
}

// Yalnız verilən dilin məqalələri, yenidən köhnəyə (pubDate-ə görə).
export async function getPosts(lang: Lang): Promise<BlogPost[]> {
  const posts = await getCollection('blog')
  assertTranslated(posts)
  return posts
    .filter((post) => postLang(post) === lang)
    .sort((a, b) => b.data.pubDate.valueOf() - a.data.pubDate.valueOf())
}

// ---------- Kateqoriyalar ----------

// Bütün kateqoriyalar (siyahıdakı düymələrin sırası). src/content/config.ts-dəki `category` enum-u ilə
// EYNİ olmalıdır: orada yeni kateqoriya əlavə etsən, burada və ui.ts-dəki blog.cat.* açarlarında da əlavə et.
export const categories = ['davamiyyet', 'hr', 'telimat', 'yenilikler'] as const
export type BlogCategory = (typeof categories)[number]

// Kateqoriyanın ui.ts-dəki ad açarı (hər dildə öz adı: t(categoryKey[cat])).
export const categoryKey: Record<BlogCategory, UIKey> = {
  davamiyyet: 'blog.cat.davamiyyet',
  hr: 'blog.cat.hr',
  telimat: 'blog.cat.telimat',
  yenilikler: 'blog.cat.yenilikler',
}

// Dəqiqə ilə oxu müddəti: mətndəki söz sayı / dəqiqədə 200 söz, yuxarı yuvarlaq, ən azı 1.
const WORDS_PER_MINUTE = 200
export function readingMinutes(post: BlogPost): number {
  const words = (post.body ?? '').split(/\s+/).filter(Boolean).length
  return Math.max(1, Math.ceil(words / WORDS_PER_MINUTE))
}

// ---------- Əlaqəli məqalələr ----------

// «Bunları da oxuyun» üçün seçim. `posts` həmin dilin tarixə görə sıralanmış siyahısıdır (getPosts).
// Cari məqalə çıxarılır, eyni kateqoriyalılar əvvələ düşür; sayı `limit`-dən azdırsa qalan boşluq digər
// ən yeni məqalələrlə doldurulur.
export function getRelatedPosts(posts: BlogPost[], current: BlogPost, limit = 2): BlogPost[] {
  const others = posts.filter((post) => post.id !== current.id)
  const sameCategory = others.filter((post) => post.data.category === current.data.category)
  const rest = others.filter((post) => post.data.category !== current.data.category)
  return [...sameCategory, ...rest].slice(0, limit)
}
