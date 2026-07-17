import { describe, expect, it } from 'vitest'
import { parseBulkText } from './bulkParse'

// The paste box is positional, so every test here is really about one question: does field N end up
// in field N? A shift is silent — the import succeeds and the wrong data is in the database.

describe('parseBulkText', () => {
  it('reads the original three-field format unchanged', () => {
    // Everything anyone has ever pasted looks like this. It must keep working exactly as before,
    // which is why the new fields were appended rather than slotted in where the form shows them.
    expect(parseBulkText('Əli Vəliyev, 0501234567, Bağban')).toEqual([
      { fullName: 'Əli Vəliyev', phoneNumber: '0501234567', position: 'Bağban' },
    ])
  })

  it('reads a name and phone only', () => {
    expect(parseBulkText('Əli Vəliyev, 0501234567')).toEqual([
      { fullName: 'Əli Vəliyev', phoneNumber: '0501234567' },
    ])
  })

  it('reads every field', () => {
    expect(parseBulkText('Rəşad Quliyev, 0701112233, Bağban, Səməd oğlu, 1990, r@mail.az, Menecer, Baş ofis')).toEqual([
      {
        fullName: 'Rəşad Quliyev',
        phoneNumber: '0701112233',
        position: 'Bağban',
        fatherName: 'Səməd oğlu',
        birthYear: 1990,
        email: 'r@mail.az',
        roleName: 'Menecer',
        locationName: 'Baş ofis',
      },
    ])
  })

  it('keeps later fields in place when a middle one is skipped', () => {
    // THE regression. The splitter used /[,\t;]+/ — a RUN of separators — so the empty cells here
    // collapsed and the email arrived as the position, with everything after it shifted left.
    const [row] = parseBulkText('Əli Vəliyev, 0501234567, , , , ali@mail.az')
    expect(row.position).toBeUndefined()
    expect(row.fatherName).toBeUndefined()
    expect(row.birthYear).toBeUndefined()
    expect(row.email).toBe('ali@mail.az')
  })

  it('reads a tab-separated paste straight out of Excel', () => {
    const [row] = parseBulkText('Əli Vəliyev\t0501234567\tBağban\tSəməd oğlu\t1990')
    expect(row).toEqual({
      fullName: 'Əli Vəliyev',
      phoneNumber: '0501234567',
      position: 'Bağban',
      fatherName: 'Səməd oğlu',
      birthYear: 1990,
    })
  })

  it('keeps empty Excel cells aligned', () => {
    // Excel gives consecutive tabs for empty cells — same collapse hazard as commas.
    const [row] = parseBulkText('Əli Vəliyev\t0501234567\t\t\t\tali@mail.az')
    expect(row.email).toBe('ali@mail.az')
    expect(row.position).toBeUndefined()
  })

  it('ignores a birth year that is not one', () => {
    // The cell is free text; "bilinmir" must not become a year, and it must not shift Email either.
    const [row] = parseBulkText('Əli Vəliyev, 0501234567, Bağban, Səməd oğlu, bilinmir, ali@mail.az')
    expect(row.birthYear).toBeUndefined()
    expect(row.email).toBe('ali@mail.az')
  })

  it('drops blank lines and lines with no name', () => {
    expect(parseBulkText('Əli Vəliyev, 050\n\n  \n, 0557654321, Mühasib')).toHaveLength(1)
  })

  it('reads several lines', () => {
    const rows = parseBulkText('Əli Vəliyev, 0501234567\nAyşə Məmmədova, 0557654321, Mühasib')
    expect(rows.map((r) => r.fullName)).toEqual(['Əli Vəliyev', 'Ayşə Məmmədova'])
  })
})
