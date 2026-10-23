import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * The board's memoisation, guarded at the one place it can be broken by accident.
 *
 * `TodayRow` is wrapped in `React.memo` because Bakı Abadlıq puts 488 people on this table and
 * nothing virtualises it: pressing a status card re-rendered every row and the board visibly froze.
 * Memo only holds while the props are stable, and the easiest way to lose that — the way anybody
 * would, adding a feature in a hurry — is to pass `onPhoto={() => doSomething(r)}` at the call site.
 * One inline arrow is a new identity on every render, memo misses on all 488 rows, and the freeze is
 * back with nothing on screen to say why.
 *
 * So it is checked here rather than trusted to a comment. A source test is unusual; a performance fix
 * that silently un-fixes itself is worse.
 */
const SOURCE = readFileSync(fileURLToPath(new URL('./TodayPage.tsx', import.meta.url)), 'utf8')

/** The `<TodayRow … />` element exactly as the page writes it. */
function callSite(): string {
  const start = SOURCE.indexOf('<TodayRow')
  expect(start, 'TodayPage must still render <TodayRow>').toBeGreaterThan(-1)
  const end = SOURCE.indexOf('/>', start)
  expect(end, 'the <TodayRow> element must be self-closing').toBeGreaterThan(start)
  return SOURCE.slice(start, end)
}

describe('TodayRow call site', () => {
  it.each(['onPosition', 'onLocation', 'onReason', 'onPhoto'])(
    '%s is a stable reference, not an inline arrow',
    (prop) => {
      const line = callSite().split('\n').find((l) => l.trim().startsWith(`${prop}=`))
      expect(line, `${prop} must be passed to TodayRow`).toBeDefined()
      // `onPhoto={onPhoto}` passes; `onPhoto={() => …}` and `onPhoto={r => …}` do not.
      expect(line).not.toMatch(/=>/)
      expect(line!.trim()).toMatch(new RegExp(`^${prop}=\\{[A-Za-z_$][\\w$.]*\\}$`))
    },
  )

  it('every handler it passes is declared with useCallback', () => {
    for (const fn of ['openReasonMenu', 'pickPosition', 'pickLocation', 'onPhoto']) {
      expect(SOURCE, `${fn} must be memoised, or the rows re-render on every pass`)
        .toMatch(new RegExp(`const ${fn} = useCallback`))
    }
  })

  it('the reason MENU is not rendered inside the row', () => {
    // It is position:fixed and only ever one is open, so it belongs at page level — inside the row it
    // was a conditional branch 488 rows had to carry.
    const row = readFileSync(fileURLToPath(new URL('./TodayRow.tsx', import.meta.url)), 'utf8')
    expect(row).not.toContain('reason-pop')
    expect(SOURCE).toContain('reason-pop')
  })
})
