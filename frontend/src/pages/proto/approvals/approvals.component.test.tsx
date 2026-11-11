// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, describe, expect, it } from 'vitest'
import { buildWorld, PERSONA_IDS } from './data'
import { ProtoApp } from './ProtoApp'

/**
 * The prototype's flows in a browser-like DOM, on the real components and router, with the demo «server» answering
 * at once (latency 0). What is asserted is what a person would see and be able to press.
 */

afterEach(cleanup)

function open(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/prototip/tesdiq/*" element={<ProtoApp latencyMs={0} loadMs={0} />} />
      </Routes>
    </MemoryRouter>,
  )
}

const world = buildWorld()
const aeroKeeper = world.personas.find((p) => p.kind === 'timekeeper' && world.branches.find((b) => b.id === p.branchId)?.structureId === 's-aero')!

async function click(el: Element) {
  await act(async () => { fireEvent.click(el) })
}

describe('Struktur xəritəsi', () => {
  it('opens Company → Structure → Branch → people, each level with its own appointments', async () => {
    open('/prototip/tesdiq/struktur?rol=admin')
    expect(await screen.findByRole('heading', { level: 1, name: 'Struktur xəritəsi' })).toBeTruthy()
    const structures = screen.getByRole('region', { name: 'Strukturlar' })
    await click(within(structures).getByText('Şəhər parkları'))
    const branches = screen.getByRole('region', { name: 'Filiallar' })
    expect(within(branches).getByText('Orxan Hüseynzadə')).toBeTruthy()          // the structure's Operator
    expect(within(branches).getByText('Kamran Abbaslı')).toBeTruthy()            // the structure's Viewer
    const firstBranch = within(branches).getAllByRole('button').find((b) => b.className.includes('apx-row'))!
    const branchName = firstBranch.querySelector('.apx-row-t')!.textContent!
    await click(firstBranch)
    const branch = screen.getByRole('region', { name: 'Filial' })
    expect(within(branch).getByRole('heading', { level: 2 }).textContent).toContain(branchName)
    expect(within(branch).getByText('Tabelçi')).toBeTruthy()
    const people = within(branch).getByRole('list', { name: 'İşçilər' })
    expect(within(people).getAllByRole('listitem').length).toBeGreaterThan(0)
    // Each person is a real button inside a list item — announced as both.
    expect(within(within(people).getAllByRole('listitem')[0]).getByRole('button')).toBeTruthy()
  })

  it('keeps the 21 undecided branches apart, placed in no structure', async () => {
    open('/prototip/tesdiq/struktur?rol=admin')
    await click(await screen.findByText('Struktursuz filiallar'))
    const branches = screen.getByRole('region', { name: 'Filiallar' })
    expect(within(branches).getByText(/Təxminlə heç bir struktura yazılmayıb/)).toBeTruthy()
    expect(within(branches).getByText('21 filial')).toBeTruthy()
  })

  it('an Operator sees only their own structure', async () => {
    open('/prototip/tesdiq/struktur?rol=op-aero')
    const structures = await screen.findByRole('region', { name: 'Strukturlar' })
    expect(within(structures).getByText('Aeroport yolu')).toBeTruthy()
    expect(within(structures).queryByText('Şəhər parkları')).toBeNull()
    expect(within(structures).queryByText('Struktursuz filiallar')).toBeNull()
  })

  it('a person opens with their details and their requests', async () => {
    open('/prototip/tesdiq/struktur?rol=op-aero')
    const branch = await screen.findByRole('region', { name: 'Filial' })
    const people = within(branch).getByRole('list', { name: 'İşçilər' })
    await click(within(within(people).getAllByRole('listitem')[0]).getByRole('button'))
    const sheet = screen.getByRole('dialog')
    expect(within(sheet).getByText('Davamiyyət tarixçəsi')).toBeTruthy()
    expect(within(sheet).getByRole('button', { name: 'Bu işçi üçün müraciət yarat' })).toBeTruthy()
  })
})

describe('a request from the branch to a decision', () => {
  it('a Timekeeper cannot send without a reason; with one it waits for the Operator, who approves it', async () => {
    open(`/prototip/tesdiq/tabelci?rol=tk-${aeroKeeper.branchId}`)
    await click(await screen.findByRole('button', { name: 'Yeni müraciət' }))
    const sheet = screen.getByRole('dialog')
    await click(within(sheet).getByRole('button', { name: /Məzuniyyət/ }))
    // A Timekeeper has one branch, so the first select in the sheet is the person.
    const employee = within(sheet).getAllByRole('combobox')[0] as HTMLSelectElement
    const firstPerson = [...employee.options].find((o) => o.value)!
    await act(async () => { fireEvent.change(employee, { target: { value: firstPerson.value } }) })
    await act(async () => { fireEvent.change(within(sheet).getByLabelText('Başlanğıc'), { target: { value: '2026-12-01' } }) })
    await act(async () => { fireEvent.change(within(sheet).getByLabelText('Bitmə'), { target: { value: '2026-12-12' } }) })

    // No reason yet: refused in place, nothing sent.
    await click(within(sheet).getByRole('button', { name: 'Operatora göndər' }))
    expect(within(sheet).getByText(/Qeyd və ya səbəb yazın/)).toBeTruthy()
    expect(screen.getByRole('dialog')).toBeTruthy()

    // The label now also carries its error line, so match its start.
    await act(async () => { fireEvent.change(within(sheet).getByLabelText(/^Qeyd \/ Səbəb/), { target: { value: 'İllik məzuniyyət' } }) })
    await click(within(sheet).getByRole('button', { name: 'Operatora göndər' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect(screen.getByText(/Müraciət göndərildi\. Demo: serverə heç nə göndərilmədi\./)).toBeTruthy()
    const mine = screen.getByRole('region', { name: 'Müraciətlərim' })
    expect(within(mine).getAllByText('Gözləyir').length).toBeGreaterThan(0)

    // The Operator of the structure, in the same tab: the request is in their queue, and approving applies it.
    await act(async () => { fireEvent.change(screen.getByLabelText('Kimin gözü ilə baxırsınız'), { target: { value: PERSONA_IDS.opAero } }) })
    await click(await screen.findByRole('link', { name: /Təsdiq mərkəzi/ }))
    const list = await screen.findByRole('list', { name: 'Müraciət siyahısı' })
    // The queue is oldest first, so the request just made is the last one about this person.
    const name = firstPerson.textContent!.replace(' (deaktiv)', '')
    const row = within(list).getAllByRole('listitem').filter((li) => li.textContent?.includes(name)).pop()!
    await click(within(row).getByRole('button'))
    const detail = screen.getByRole('region', { name: 'Müraciətin detalları' })
    await click(within(detail).getByRole('button', { name: 'Təsdiqlə' }))
    await waitFor(() => expect(within(detail).getByText('Tətbiq olundu')).toBeTruthy())
    expect(within(detail).getAllByText('Təsdiqləndi').length).toBeGreaterThan(0)
    expect(within(detail).queryByRole('button', { name: 'Təsdiqlə' })).toBeNull()
  })

  it('a rejection needs a reason, and the reason shows on the request', async () => {
    const r = world.requests.find((x) => x.status === 'pending' && world.branches.find((b) => b.id === x.branchId)?.structureId === 's-aero')!
    open(`/prototip/tesdiq/muracietler?rol=op-aero&id=${r.id}`)
    const detail = await screen.findByRole('region', { name: 'Müraciətin detalları' })
    await click(within(detail).getByRole('button', { name: 'Rədd et' }))
    await click(within(detail).getByRole('button', { name: 'Rəddi təsdiqlə' }))
    expect(within(detail).getByText(/Rədd səbəbini yazın/)).toBeTruthy()
    await act(async () => { fireEvent.change(within(detail).getByLabelText(/Rədd səbəbi/), { target: { value: 'Arayış əlavə olunmayıb' } }) })
    await click(within(detail).getByRole('button', { name: 'Rəddi təsdiqlə' }))
    await waitFor(() => expect(within(detail).getAllByText('Rədd edildi').length).toBeGreaterThan(0))
    expect(within(detail).getAllByText(/Arayış əlavə olunmayıb/).length).toBeGreaterThan(0)
  })

  it('a Timekeeper sees their request but is not offered a decision on it', async () => {
    const r = world.requests.find((x) => x.status === 'pending' && x.createdById === aeroKeeper.id)!
    open(`/prototip/tesdiq/muracietler?rol=tk-${aeroKeeper.branchId}&id=${r.id}`)
    const detail = await screen.findByRole('region', { name: 'Müraciətin detalları' })
    expect(within(detail).queryByRole('button', { name: 'Təsdiqlə' })).toBeNull()
    expect(within(detail).getByRole('button', { name: 'Müraciəti ləğv et' })).toBeTruthy()
    expect(within(detail).getByText(/Öz müraciətinizi təsdiqləyə bilməzsiniz/)).toBeTruthy()
  })
})

describe('Viewer', () => {
  it('reads the dashboard with nothing to press, and a request without a decision', async () => {
    open('/prototip/tesdiq/musahideci?rol=vw-aero')
    expect(await screen.findByText('Yalnız baxış')).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Yeni|Təsdiqlə|Rədd et|Ləğv et/ })).toBeNull()
    cleanup()

    const r = world.requests.find((x) => x.status === 'pending' && world.branches.find((b) => b.id === x.branchId)?.structureId === 's-aero')!
    open(`/prototip/tesdiq/muracietler?rol=vw-aero&id=${r.id}`)
    const detail = await screen.findByRole('region', { name: 'Müraciətin detalları' })
    expect(within(detail).queryByRole('button', { name: 'Təsdiqlə' })).toBeNull()
    expect(within(detail).queryByRole('button', { name: 'Rədd et' })).toBeNull()
    expect(within(detail).getByText(/Siz müşahidəçisiniz/)).toBeTruthy()
  })
})

describe('states', () => {
  it('«Xəta rejimi» shows the error state with a way to retry', async () => {
    open('/prototip/tesdiq/muracietler?rol=op-aero')
    await screen.findByRole('region', { name: 'Müraciətlər' })
    await click(screen.getByLabelText('Xəta rejimi'))
    expect(await screen.findByText('Müraciətlər yüklənmədi')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Yenidən yüklə' })).toBeTruthy()
  })

  it('a filter with no match says so and offers to reset', async () => {
    open('/prototip/tesdiq/muracietler?rol=op-aero')
    await act(async () => { fireEvent.change(await screen.findByLabelText('Müraciət axtar'), { target: { value: 'heç-kim-belə-deyil' } }) })
    expect(screen.getByText('Bu filtrə uyğun müraciət yoxdur')).toBeTruthy()
    await click(screen.getByRole('button', { name: 'Filtrləri sıfırla' }))
    expect(screen.queryByText('Bu filtrə uyğun müraciət yoxdur')).toBeNull()
  })
})
