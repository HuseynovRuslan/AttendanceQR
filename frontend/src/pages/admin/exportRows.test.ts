import { describe, expect, it } from 'vitest'
import { cellTime, exportRow, type ExportableRow, areaOf, uniqueAreas } from './exportRows'

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
