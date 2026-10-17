import { createRequire } from 'node:module'
import { existsSync, readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs'
import { extname, join } from 'node:path'
import type { FullResult, Reporter } from '@playwright/test/reporter'
import { e2eSettings } from './env'

// Last reporter in the list: by the time it runs, the HTML report has copied every trace. It then
// takes credentials out of everything the run left behind, and fails the run if anything survives.
//
// Why it is needed at all: a trace records each request with its headers, so every API call made by
// a signed-in page carries "Authorization: Bearer <JWT>" into the zip — and these JWTs live for a
// century (JwtOptions.ExpiryMinutes). The PINs themselves never get this far (they are entered
// through evaluate() in specs that run untraced, and setup logs in with plain fetch), so for them
// this is the second lock, not the first.

const require = createRequire(import.meta.url)
// Playwright's own zip reader/writer — exported by playwright-core, so no extra dependency.
// Typed here with just what is used (the bundle ships no declarations).
type ReadStream = NodeJS.ReadableStream
interface ZipEntry { fileName: string }
interface ZipReader extends NodeJS.EventEmitter {
  readEntry(): void
  openReadStream(entry: ZipEntry, cb: (err: Error | null, stream?: ReadStream) => void): void
}
interface ZipWriter {
  addBuffer(data: Buffer, name: string): void
  end(): void
  outputStream: ReadStream
}
const { yauzl, yazl } = require('playwright-core/lib/zipBundle') as {
  yauzl: { fromBuffer(buf: Buffer, opts: { lazyEntries: boolean }, cb: (err: Error | null, zip?: ZipReader) => void): void }
  yazl: { ZipFile: new () => ZipWriter }
}

type Entry = { name: string; data: Buffer }

const JWT = /eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}/g
// A credential field in a JSON body, plain or escaped inside another JSON string (trace postData).
const KEYED = /(\\?"(?:password|newPin|pin|tempPin|currentPassword|activationToken)\\?"\s*:\s*\\?")([^"\\]*)(\\?")/gi

// The text of the box a temporary PIN or an invite link is shown in, as a DOM snapshot records it.
const LINK_BOX = /(\["[A-Z]+",\{"class":"link-box"[^}]*\},")([^"]+)(")/g

/** Phone numbers of the accounts in use, in every spelling the API accepts (last 9 digits match). */
function phonePatterns(): RegExp[] {
  const cores = new Set<string>()
  for (const [k, v] of Object.entries(e2eSettings())) {
    if (/^E2E_.*_PHONE$/.test(k) && v) {
      const digits = v.replace(/\D/g, '')
      if (digits.length >= 9) cores.add(digits.slice(-9))
    }
  }
  return [...cores].map((c) => {
    const spaced = c.split('').join('[\\s-]?')
    return new RegExp(`(?<![0-9])(?:\\+?994[\\s-]?|0)?${spaced}(?![0-9])`, 'g')
  })
}

/** Logins that are e-mail addresses (the admin's, read in setup) — redacted wherever they appear. */
function emailPatterns(): RegExp[] {
  return Object.entries(e2eSettings())
    .filter(([k, v]) => /^E2E_.*_EMAIL$/.test(k) && v && v.includes('@'))
    .map(([, v]) => new RegExp(v!.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'gi'))
}

function scrubText(text: string, phones: RegExp[]): { text: string; hits: number } {
  let hits = 0
  let out = text.replace(JWT, () => (hits++, 'eyJ***REDACTED***'))
  out = out.replace(KEYED, (m, a: string, v: string, b: string) => (v && v !== '***' ? (hits++, `${a}***${b}`) : m))
  out = out.replace(LINK_BOX, (m, a: string, v: string, b: string) => (v !== '***' ? (hits++, `${a}***${b}`) : m))
  for (const p of phones) out = out.replace(p, () => (hits++, '***'))
  return { text: out, hits }
}

function isText(name: string, data: Buffer): boolean {
  if (/\.(png|jpe?g|webm|webp|gif|woff2?|ttf|ico)$/i.test(name)) return false
  return !data.subarray(0, 8192).includes(0)
}

function readZip(buf: Buffer): Promise<Entry[]> {
  return new Promise((resolve, reject) => {
    yauzl.fromBuffer(buf, { lazyEntries: true }, (err, zip) => {
      if (err || !zip) return reject(err)
      const out: Entry[] = []
      zip.on('entry', (entry: ZipEntry) => {
        if (entry.fileName.endsWith('/')) return zip.readEntry()
        zip.openReadStream(entry, (e, stream) => {
          if (e || !stream) return reject(e)
          const chunks: Buffer[] = []
          stream.on('data', (c: Buffer) => chunks.push(c))
          stream.on('error', reject)
          stream.on('end', () => {
            out.push({ name: entry.fileName, data: Buffer.concat(chunks) })
            zip.readEntry()
          })
        })
      })
      zip.on('end', () => resolve(out))
      zip.on('error', reject)
      zip.readEntry()
    })
  })
}

function writeZip(entries: Entry[]): Promise<Buffer> {
  const zip = new yazl.ZipFile()
  for (const e of entries) zip.addBuffer(e.data, e.name)
  zip.end()
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = []
    zip.outputStream.on('data', (c: Buffer) => chunks.push(c))
    zip.outputStream.on('end', () => resolve(Buffer.concat(chunks)))
    zip.outputStream.on('error', reject)
  })
}

async function scrubZip(buf: Buffer, phones: RegExp[]): Promise<{ buf: Buffer; hits: number }> {
  const entries = await readZip(buf)
  let hits = 0
  for (const e of entries) {
    if (!isText(e.name, e.data)) continue
    const r = scrubText(e.data.toString('utf8'), phones)
    if (r.hits) {
      hits += r.hits
      e.data = Buffer.from(r.text, 'utf8')
    }
  }
  return { buf: hits ? await writeZip(entries) : buf, hits }
}

function* walk(dir: string): Generator<string> {
  if (!existsSync(dir)) return
  for (const name of readdirSync(dir)) {
    const p = join(dir, name)
    if (statSync(p).isDirectory()) yield* walk(p)
    else yield p
  }
}

const EMBEDDED = /(data:application\/zip;base64,)([A-Za-z0-9+/=]+)/

export async function scrubArtifacts(dirs: string[]): Promise<{ files: number; hits: number }> {
  const phones = [...phonePatterns(), ...emailPatterns()]
  let files = 0
  let hits = 0
  for (const dir of dirs) {
    for (const file of walk(dir)) {
      // The trace viewer's own static files never contain run data.
      if (file.includes(`${join('playwright-report', 'trace')}`)) continue
      const ext = extname(file).toLowerCase()
      const data = readFileSync(file)
      let r: { buf: Buffer; hits: number } | undefined
      if (ext === '.zip') {
        r = await scrubZip(data, phones)
      } else if (ext === '.html' && EMBEDDED.test(data.toString('utf8'))) {
        // The HTML report keeps its results as a base64 zip inside index.html.
        const html = data.toString('utf8')
        const m = EMBEDDED.exec(html)!
        const inner = await scrubZip(Buffer.from(m[2], 'base64'), phones)
        r = { buf: Buffer.from(html.replace(EMBEDDED, `$1${inner.buf.toString('base64')}`), 'utf8'), hits: inner.hits }
      } else if (isText(file, data)) {
        const t = scrubText(data.toString('utf8'), phones)
        r = { buf: Buffer.from(t.text, 'utf8'), hits: t.hits }
      }
      if (r?.hits) {
        writeFileSync(file, r.buf)
        files++
        hits += r.hits
      }
    }
  }
  return { files, hits }
}

export default class ScrubReporter implements Reporter {
  printsToStdio() {
    return false
  }

  async onEnd(_result: FullResult): Promise<{ status?: FullResult['status'] } | void> {
    const dirs = ['test-results', 'playwright-report']
    const first = await scrubArtifacts(dirs)
    // Second pass must find nothing: if it does, something is in a shape the first pass cannot
    // clean, and the run is failed rather than leaving credentials behind silently.
    const second = await scrubArtifacts(dirs)
    console.log(`\n[e2e] Artefaktlar təmizləndi: ${first.hits} gizli dəyər ${first.files} faylda silindi.`)
    if (second.hits) {
      console.log(`[e2e] DİQQƏT: ikinci yoxlamada ${second.hits} dəyər qaldı — run uğursuz sayılır.`)
      return { status: 'failed' }
    }
  }
}
