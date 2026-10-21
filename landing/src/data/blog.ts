import { getCollection, type CollectionEntry } from 'astro:content'
import { localizedPath, languages, type Lang, type UIKey } from '../i18n/ui'

// Məqalələr src/content/blog/{az,ru,en}/ qovluqlarındadır; `id` "dil/slug" formasındadır (məs. 'az/saxta-giris').
// Slug hər dildə eynidir: dil seçici bununla məqalənin tərcüməsinə aparır.

export type BlogPost = CollectionEntry<'blog'>

export function postLang(post: BlogPost): Lang {
  return post.id.split('/')[0] as Lang
}

export function postSlug(post: BlogPost): string {
  return post.id.split('/').slice(1).join('/')
}

// blogPath('ru') → '/ru/blog/', blogPath('ru', 'saxta-giris') → '/ru/blog/saxta-giris/'.
export function blogPath(lang: Lang, slug?: string): string {
  return localizedPath(lang, slug ? `/blog/${slug}/` : '/blog/')
}

export const dateLocale: Record<Lang, string> = { az: 'az-AZ', ru: 'ru-RU', en: 'en-US' }

// Hər məqalənin üç dildə eyni slug-lı qarşılığı olmalıdır, yoxsa dil seçici 404-ə aparar: build dayanır.
function assertTranslated(posts: BlogPost[]): void {
  const slugsByLang = new Map<Lang, Set<string>>(
    languages.map((lang) => [lang, new Set(posts.filter((p) => postLang(p) === lang).map(postSlug))]),
  )
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

// Verilən dilin məqalələri, yenidən köhnəyə.
export async function getPosts(lang: Lang): Promise<BlogPost[]> {
  const posts = await getCollection('blog')
  assertTranslated(posts)
  return posts
    .filter((post) => postLang(post) === lang)
    .sort((a, b) => b.data.pubDate.valueOf() - a.data.pubDate.valueOf())
}

// Düymələrin sırası. src/content/config.ts-dəki `category` enum-u və ui.ts-dəki blog.cat.* açarları ilə eyni olmalıdır.
export const categories = ['davamiyyet', 'hr', 'telimat', 'yenilikler'] as const
export type BlogCategory = (typeof categories)[number]

export const categoryKey: Record<BlogCategory, UIKey> = {
  davamiyyet: 'blog.cat.davamiyyet',
  hr: 'blog.cat.hr',
  telimat: 'blog.cat.telimat',
  yenilikler: 'blog.cat.yenilikler',
}

const WORDS_PER_MINUTE = 200
export function readingMinutes(post: BlogPost): number {
  const words = (post.body ?? '').split(/\s+/).filter(Boolean).length
  return Math.max(1, Math.ceil(words / WORDS_PER_MINUTE))
}

// «Bunları da oxuyun»: eyni kateqoriyalılar əvvəldə, çatmasa digər ən yenilərlə tamamlanır.
export function getRelatedPosts(posts: BlogPost[], current: BlogPost, limit = 2): BlogPost[] {
  const others = posts.filter((post) => post.id !== current.id)
  const sameCategory = others.filter((post) => post.data.category === current.data.category)
  const rest = others.filter((post) => post.data.category !== current.data.category)
  return [...sameCategory, ...rest].slice(0, limit)
}
