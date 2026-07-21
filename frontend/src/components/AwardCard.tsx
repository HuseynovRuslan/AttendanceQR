import { useEffect, useState } from 'react'
import { getMyAwards, type MyAward } from '../api/vote'

const MONTHS = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'İyun',
  'İyul', 'Avqust', 'Sentyabr', 'Oktyabr', 'Noyabr', 'Dekabr',
]

const monthOf = (period: string) => MONTHS[Number(period.slice(5, 7)) - 1] ?? ''
const yearOf = (period: string) => period.slice(0, 4)

/**
 * "Ayın işçisi" won by the person looking at the screen.
 *
 * The award was being decided, recorded and announced company-wide, and the winner's own app said
 * nothing — they found out by reading their name in a list sent to everyone. A trophy nobody can see
 * afterwards isn't much of a trophy, so it stays on their home screen for good.
 */
export function AwardCard() {
  const [awards, setAwards] = useState<MyAward[]>([])

  useEffect(() => {
    void getMyAwards().then(({ status, data }) => {
      if (status === 200 && Array.isArray(data)) setAwards(data)
    })
  }, [])

  if (awards.length === 0) return null

  // The API returns newest first.
  const latest = awards[0]
  const earlier = awards.length - 1

  return (
    <div className="rounded-3xl border border-amber-200 bg-gradient-to-r from-amber-50 to-yellow-50 p-5 shadow-sm">
      <div className="flex items-center gap-4">
        <div className="text-5xl">🏆</div>
        <div className="min-w-0">
          <div className="font-bold text-amber-900">
            {monthOf(latest.period)} {yearOf(latest.period)} — ayın işçisi
          </div>
          <p className="mt-1 text-sm text-amber-800">
            Komandanız sizə {latest.votes} səs verdi. Təbrik edirik!
          </p>
          {earlier > 0 && (
            <p className="mt-1 text-xs text-amber-700">
              Bu mükafatı {awards.length} dəfə qazanmısınız.
            </p>
          )}
        </div>
      </div>
    </div>
  )
}
