import { describe, expect, it } from 'vitest'
import { boardExitOf, cellTime, exitOf, exportRow, hasOpenSession, sessionsText, workedText, type ExportableRow, areaOf, sameArea, uniqueAreas, unlistedAreas } from './exportRows'

// Baku is UTC+4. Every case below is one an adversarial audit of the morning report actually found:
// a night that read backwards, a carried-over night that read as this morning, a field worker with no
// times at all. A spreadsheet cell has no badge and no tooltip — whatever the board conveys by
// context has to be written into the cell or the leadership never sees it.

const row = (over: Partial<ExportableRow>): ExportableRow => ({
  employeeName: 'Xaliqov İsa',
  locationName: 'Qala Anbar',
  status: 'OnTime',
  ...over,
})

describe('cellTime', () => {
  it('prints a plain clock time when the scan is on the exported day', () => {
    // 16:06Z = 20:06 Baku.
    expect(cellTime('2026-09-05T16:06:00Z', '2026-09-05')).toBe('20:06')
  })

  it('marks a night shift check-out as the NEXT morning', () => {
    // The bug: «20:06 → 05:21» read as a check-out before the check-in.
    expect(cellTime('2026-09-06T01:21:00Z', '2026-09-05')).toBe('05:21 (+1)')
  })

  it('marks a carried-over night check-in as the PREVIOUS evening', () => {
    // The board carries an open night onto today's row until the shift's window closes; exported
    // bare, «21:33» read as though he had arrived this morning.
    expect(cellTime('2026-09-05T17:33:00Z', '2026-09-06')).toBe('21:33 (-1)')
  })

  it('is empty when there is no scan — the board\'s cell is empty too, not a dash', () => {
    expect(cellTime(null, '2026-09-05')).toBe('')
    expect(cellTime(undefined, '2026-09-05')).toBe('')
  })
})

describe('exportRow', () => {
  it('takes a field day\'s times from the field visit, as the board does', () => {
    // «Sahədə»: there is NO attendance record, so checkInAtUtc is null by definition. This row used
    // to export as «Sahədə — —» while being counted under «Tamamlayıb» on the summary sheet.
    const out = exportRow(
      row({
        status: 'Field',
        checkInAtUtc: null,
        checkOutAtUtc: null,
        fieldCheckInAtUtc: '2026-09-07T04:40:00Z',   // 08:40 Baku
        fieldCheckOutAtUtc: '2026-09-07T13:05:00Z',  // 17:05 Baku
      }),
      '2026-09-07',
      'Sahədə',
    )

    expect(out.checkIn).toBe('08:40')
    expect(out.checkOut).toBe('17:05')
    expect(out.bucket).toBe('present')   // counted as arrived — and now with the times to match
    expect(out.photo).toBe('—')          // field visits carry no selfie by design
  })

  it('says when a time came off a phone that was offline', () => {
    const out = exportRow(
      row({ checkInAtUtc: '2026-09-07T04:02:00Z', wasOffline: true }),
      '2026-09-07',
      'İşdə',
    )
    expect(out.checkIn).toBe('08:02 (oflayn)')
  })

  it('keeps the late and early reasons the board shows', () => {
    const out = exportRow(
      row({
        checkInAtUtc: '2026-09-07T05:30:00Z',
        checkOutAtUtc: '2026-09-07T12:00:00Z',
        lateArrivalReason: 'yol bağlı idi',
        earlyDepartureReason: 'həkim',
      }),
      '2026-09-07',
      'Tamamlayıb',
    )
    expect(out.checkIn).toBe('09:30 (gec: yol bağlı idi)')
    expect(out.checkOut).toBe('16:00 (tez: həkim)')
  })

  it('carries the leave kinds apart, because the status cannot', () => {
    // Ezamiyyət is WORK. It reached the client as OnLeave and was exported as annual leave once.
    expect(exportRow(row({ status: 'OnLeave', leaveType: 'BusinessTrip' }), '2026-09-07', 'Ezamiyyət').bucket)
      .toBe('trip')
    expect(exportRow(row({ status: 'OnLeave', leaveType: 'Sick' }), '2026-09-07', 'Xəstəlik').bucket)
      .toBe('sick')
    expect(exportRow(row({ status: 'OnLeave', leaveType: 'Vacation' }), '2026-09-07', 'Məzuniyyət').bucket)
      .toBe('onLeave')
  })

  it('an absent person has empty time cells and no invented photo mark', () => {
    const out = exportRow(row({ status: 'Absent' }), '2026-09-07', 'Qayıb')
    expect(out.checkIn).toBe('')
    expect(out.checkOut).toBe('')
    expect(out.photo).toBe('—')
    expect(out.bucket).toBe('absent')
  })
})

describe('«Faktiki» ilə «sənəd üzrə» — eyni adamlar, iki quruluş', () => {
  // HR hər səhər rəhbərliyə sənəd üzrə fayl göndərir, başqası isə faktiki istəyir. Eyni gün, eyni
  // adamlar — yalnız hesabatın forması dəyişir, ona görə bu, bir açardır, iki ayrı ixrac deyil.
  const row = { employeeName: 'Kimsə', locationName: 'Green Garden', status: 'OnTime' }

  it('sənəd üzrə görünüşdə adam sənədinin göstərdiyi əraziyə düşür', () => {
    expect(areaOf({ ...row, paperSite: 'Nərimanov Ofis' }, 'paper')).toBe('Nərimanov Ofis')
  })

  it('sənədi yazılmayan adam öz filialında qalır — fərq yalnız yazılanlarda olur', () => {
    // Bu geriyə düşmə qaydanın özüdür: demək olar hamıda sənəd filialla üst-üstə düşür, və boş
    // sahə «bu adamın yeri yoxdur» yox, «fərq yoxdur» deməkdir.
    expect(areaOf({ ...row, paperSite: null }, 'paper')).toBe('Green Garden')
    expect(areaOf(row, 'paper')).toBe('Green Garden')
  })

  it('faktiki görünüş sənədə heç vaxt baxmır', () => {
    expect(areaOf({ ...row, paperSite: 'Nərimanov Ofis' }, 'actual')).toBe('Green Garden')
  })

  it('fayl seçilmiş görünüşə görə qruplaşır — sətrin «location» xanası budur', () => {
    // Kitabça hansı Location verilirsə ona görə qruplaşdırır: həm «Xülasə» sətirləri, həm
    // «Davamiyyət» bannerleri. Yəni görünüşü burada seçmək bütün faylı yenidən formalaşdırır.
    const r = { ...row, paperSite: 'Nərimanov Ofis' }
    expect(exportRow(r, '2026-09-09', 'Tamamlayıb', 'paper').location).toBe('Nərimanov Ofis')
    expect(exportRow(r, '2026-09-09', 'Tamamlayıb', 'actual').location).toBe('Green Garden')
    // Görünüş verilməyəndə köhnə davranış qalır — mövcud çağırışlar dəyişmir.
    expect(exportRow(r, '2026-09-09', 'Tamamlayıb').location).toBe('Green Garden')
  })

  it('ərazi siyahısı seçilmiş görünüşün əraziləridir, təkrarsız və sıralı', () => {
    const rows = [
      { ...row, locationName: 'Green Garden', paperSite: 'Nərimanov Ofis' },
      { ...row, locationName: 'Green Garden', paperSite: null },
      { ...row, locationName: 'Qala Anbar', paperSite: 'Nərimanov Ofis' },
    ]
    expect(uniqueAreas(rows, 'actual')).toEqual(['Green Garden', 'Qala Anbar'])
    expect(uniqueAreas(rows, 'paper')).toEqual(['Green Garden', 'Nərimanov Ofis'])
  })
})

describe('a company form for «Xülasə»', () => {
  // Bakı Abadlıq's «Davamiyyət siyahısı» (2026-10-21), cut down: two branches and one line that has
  // no branch behind it yet.
  const form = ['Qala Anbar', 'Zoopark', 'İDEA', 'Bayıl yolu']

  it('matches an area whatever the spacing or case, the way the server does', () => {
    expect(sameArea('Qala Anbar', '  qala anbar ')).toBe(true)
    // The Azerbaijani capital of «i» is «İ»; «I» is the capital of «ı».
    expect(sameArea('İDEA', 'idea')).toBe(true)
  })

  it('keeps letters that are different letters apart', () => {
    expect(sameArea('Bayıl yolu', 'Bayil yolu')).toBe(false)
    expect(sameArea('Ərazi', 'Erazi')).toBe(false)
  })

  it('names the chosen areas the form has no line for — the ones the server would refuse', () => {
    expect(unlistedAreas(['Qala Anbar', 'İDEA'], form)).toEqual([])
    expect(unlistedAreas(['Qala Anbar', 'Ramana Yaşıllıq', 'Aeroport yolu'], form))
      .toEqual(['Ramana Yaşıllıq', 'Aeroport yolu'])
  })

  it('treats a line with no branch as a line, not as a requirement', () => {
    // Choosing fewer areas than the form has lines is fine — the other lines stay blank.
    expect(unlistedAreas(['İDEA'], form)).toEqual([])
    expect(unlistedAreas([], form)).toEqual([])
  })
})

// A day worked in several sessions — «Elastik qrafik», or a split-shift crew. Baku is UTC+4.
describe('a flexible day of several sessions', () => {
  const three = row({
    isFlexible: true,
    checkInAtUtc: '2026-10-05T05:00:00Z', checkOutAtUtc: '2026-10-05T08:00:00Z',        // 09:00–12:00
    blockSpans: [
      { inAtUtc: '2026-10-05T05:00:00Z', outAtUtc: '2026-10-05T08:00:00Z' },           // 09:00–12:00
      { inAtUtc: '2026-10-05T09:00:00Z', outAtUtc: '2026-10-05T14:00:00Z' },           // 13:00–18:00
      { inAtUtc: '2026-10-05T15:00:00Z', outAtUtc: '2026-10-05T17:30:00Z' },           // 19:00–21:30
    ],
    workedMinutes: 630,
  })

  it('prints the first arrival, the LAST departure, the server hours and every session', () => {
    const out = exportRow(three, '2026-10-05', 'Tamamlayıb')
    expect(out.checkIn).toBe('09:00')
    expect(out.checkOut).toBe('21:30')            // was «12:00» — the first session's exit
    expect(out.worked).toBe('10:30')              // the breaks are not paid: not 12:30
    expect(out.sessions).toBe('09:00–12:00, 13:00–18:00, 19:00–21:30')
  })

  it('stamps a session across midnight with the next day, once', () => {
    const night = row({
      isFlexible: true,
      checkInAtUtc: '2026-10-06T05:00:00Z', checkOutAtUtc: '2026-10-06T08:00:00Z',
      blockSpans: [
        { inAtUtc: '2026-10-06T05:00:00Z', outAtUtc: '2026-10-06T08:00:00Z' },         // 09:00–12:00
        { inAtUtc: '2026-10-06T18:00:00Z', outAtUtc: '2026-10-07T02:00:00Z' },         // 22:00–06:00 (+1)
      ],
      workedMinutes: 660,
    })
    const out = exportRow(night, '2026-10-06', 'Tamamlayıb')
    expect(out.checkOut).toBe('06:00 (+1)')
    expect(out.sessions).toBe('09:00–12:00, 22:00–06:00 (+1)')
    expect(out.worked).toBe('11:00')
  })

  it('prints no departure while the last session is open, and never estimates it', () => {
    const open = row({
      isFlexible: true,
      checkInAtUtc: '2026-10-08T05:00:00Z', checkOutAtUtc: '2026-10-08T08:00:00Z',
      lastCheckOutAtUtc: null,
      blockSpans: [
        { inAtUtc: '2026-10-08T05:00:00Z', outAtUtc: '2026-10-08T08:00:00Z' },
        { inAtUtc: '2026-10-08T09:00:00Z', outAtUtc: null },
      ],
      workedMinutes: 180,
    } as Partial<ExportableRow>)
    expect(exitOf(open)).toBeNull()               // not the first session's 12:00
    expect(hasOpenSession(open)).toBe(true)
    const out = exportRow(open, '2026-10-08', 'İşdə')
    expect(out.checkOut).toBe('')
    expect(out.worked).toBe('03:00 + açıq')
    expect(out.sessions).toBe('09:00–12:00, 13:00–açıq')
  })

  it('leaves an ordinary one-session day as it was, with its hours added', () => {
    const plain = row({ checkInAtUtc: '2026-10-05T05:00:00Z', checkOutAtUtc: '2026-10-05T14:00:00Z', workedMinutes: 540 })
    const out = exportRow(plain, '2026-10-05', 'Tamamlayıb')
    expect(out.checkOut).toBe('18:00')
    expect(out.worked).toBe('09:00')
    expect(out.sessions).toBe('')
    expect(workedText(row({ checkInAtUtc: '2026-10-05T05:00:00Z', workedMinutes: 0 }))).toBe('açıq')
    expect(workedText(row({ status: 'Absent', workedMinutes: 0 }))).toBe('')
    expect(sessionsText(plain, '2026-10-05')).toBe('')
  })
})

describe('a FIXED two-block day (ikiqat gün) keeps exactly what main showed', () => {
  // 08:00–12:00 and 16:00–20:00. The first block was left at 11:00, early, with a reason — and that reason lives on
  // the FIRST block's record. Owner's decision, 17.11: a fixed shift's board and file do not change in this release.
  const closed = row({
    checkInAtUtc: '2026-10-05T04:00:00Z', checkOutAtUtc: '2026-10-05T07:00:00Z',        // 08:00–11:00
    lastCheckOutAtUtc: '2026-10-05T16:00:00Z',                                          // 20:00
    earlyDepartureReason: 'Həkimə',
    blockSpans: [
      { inAtUtc: '2026-10-05T04:00:00Z', outAtUtc: '2026-10-05T07:00:00Z' },           // 08:00–11:00
      { inAtUtc: '2026-10-05T12:00:00Z', outAtUtc: '2026-10-05T16:00:00Z' },           // 16:00–20:00
    ],
    workedMinutes: 420,
  })

  it("the file prints the first block's departure with its own early reason, as before", () => {
    expect(exitOf(closed)).toBe('2026-10-05T07:00:00Z')
    expect(exportRow(closed, '2026-10-05', 'Tamamlayıb').checkOut).toBe('11:00 (tez: Həkimə)')
  })

  it("the board shows the last block's departure once there is one, as before", () => {
    expect(boardExitOf(closed)).toBe('2026-10-05T16:00:00Z')
  })

  it("while the second block is open the board and the file both show the first block's exit, as before", () => {
    const open = { ...closed, lastCheckOutAtUtc: null, earlyDepartureReason: null,
      blockSpans: [closed.blockSpans![0], { inAtUtc: '2026-10-05T12:00:00Z', outAtUtc: null }] }
    expect(boardExitOf(open)).toBe('2026-10-05T07:00:00Z')
    expect(exitOf(open)).toBe('2026-10-05T07:00:00Z')
    expect(hasOpenSession(open)).toBe(true)   // only feeds «İşlənmiş», which a fixed-only company never gets
  })

  it("the same two blocks on a flexible shift read the last session — the rule is the shift's, not the shape's", () => {
    const flexible = { ...closed, isFlexible: true, earlyDepartureReason: null }
    expect(exitOf(flexible)).toBe('2026-10-05T16:00:00Z')
    expect(boardExitOf(flexible)).toBe('2026-10-05T16:00:00Z')
  })
})
