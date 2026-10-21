import { defineCollection, z } from 'astro:content'
import { glob } from 'astro/loaders'

// Bloq məqalələri src/content/blog/{az,ru,en}/ altında Markdown fayllarıdır.
const blog = defineCollection({
  loader: glob({ pattern: '**/*.md', base: './src/content/blog' }),
  schema: z.object({
    title: z.string(),
    description: z.string(),
    pubDate: z.coerce.date(),
    // Kateqoriya: məqalənin hansı mövzuya aid olduğu (məcburi; yalnız bu 4 dəyərdən biri).
    category: z.enum(['davamiyyet', 'hr', 'telimat', 'yenilikler']),
    // Seçilmiş: true olan məqalə siyahıda xüsusi yerdə göstərilə bilər (susmaya görə false).
    featured: z.boolean().default(false),
    // Üz şəkli: /public-dən başlayan yol, məs. "/blog/cover-saxta-giris.svg" (ixtiyari).
    cover: z.string().optional(),
    // Qısa xülasə: məqalədəki mavi «Qısaca» qutusunun mətni (ixtiyari).
    summary: z.string().optional(),
  }),
})

export const collections = { blog }
