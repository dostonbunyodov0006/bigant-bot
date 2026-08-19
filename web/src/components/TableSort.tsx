import { useMemo, useState } from 'react'

/**
 * USTUN BO'YICHA SARALASH VA HOLAT FILTRI.
 *
 * «Qaysi MChJ eng ko'p qarzdor», «qaysi bitim eng eski» — bularni ko'z
 * bilan qidirib topib bo'lmaydi. Ustun nomini bosish yetarli bo'lsin.
 *
 * Filtr ham shu yerda: tugagan shartnomalar davom etayotganlaridan
 * ajratilsin, aks holda ular bitta ro'yxatda aralashib yotadi.
 */
export type SortDir = 'asc' | 'desc'

export function useSort<T>(rows: T[], initialKey: keyof T & string, initialDir: SortDir = 'desc') {
  const [key, setKey] = useState<string>(initialKey)
  const [dir, setDir] = useState<SortDir>(initialDir)

  const sorted = useMemo(() => {
    const out = [...rows]
    out.sort((a, b) => {
      const x = (a as Record<string, unknown>)[key]
      const y = (b as Record<string, unknown>)[key]

      // Bo'sh qiymat doim oxirida — yo'q ma'lumot ro'yxat boshini egallamasin.
      if (x == null && y == null) return 0
      if (x == null) return 1
      if (y == null) return -1

      const c = typeof x === 'number' && typeof y === 'number'
        ? x - y
        : String(x).localeCompare(String(y), 'uz')

      return dir === 'asc' ? c : -c
    })
    return out
  }, [rows, key, dir])

  /** Ustun sarlavhasiga qo'yiladi. */
  const th = (k: string) => ({
    onClick: () => {
      if (k === key) setDir(d => (d === 'asc' ? 'desc' : 'asc'))
      else { setKey(k); setDir('desc') }
    },
    className: 'sortable' + (k === key ? ' on' : ''),
    'aria-sort': (k === key ? (dir === 'asc' ? 'ascending' : 'descending') : 'none') as
      'ascending' | 'descending' | 'none',
    title: 'Saralash uchun bosing',
  })

  /** Sarlavha matni yonidagi belgi. */
  const mark = (k: string) => (k === key ? (dir === 'asc' ? ' ↑' : ' ↓') : '')

  return { rows: sorted, th, mark }
}

/** Ochiq / Yopilgan / Barchasi tanlovi. */
export function StatusFilter({ value, onChange, counts }: {
  value: 'open' | 'closed' | 'all'
  onChange: (v: 'open' | 'closed' | 'all') => void
  counts: { open: number; closed: number }
}) {
  const opts = [
    { v: 'open' as const, label: 'Davom etayotgan shartnomalar', n: counts.open },
    { v: 'closed' as const, label: 'Tugagan shartnomalar', n: counts.closed },
    { v: 'all' as const, label: 'Barcha shartnomalar', n: counts.open + counts.closed },
  ]
  return (
    <div className="seg">
      {opts.map(o => (
        <button key={o.v} aria-pressed={value === o.v} onClick={() => onChange(o.v)}>
          {o.label} <span className="seg-n">{o.n}</span>
        </button>
      ))}
    </div>
  )
}

/**
 * QIDIRUV — MChJ nomi yoki shartnoma raqami bo'yicha.
 *
 * Shartnomalar ko'payganda ko'z bilan qidirish imkonsiz bo'ladi.
 * Katta-kichik harf va x/h farqi hisobga olinmaydi: «unihorm», «UNIFORM»,
 * «Uniform» — bir xil topiladi. Bot ham xuddi shu qoida bilan ishlaydi.
 */
export function fold(s: string) {
  return s.toLowerCase()
    .replace(/x/g, 'h')
    .replace(/[’'`]/g, '')
    .replace(/\s+/g, ' ')
    .trim()
}

export function Search({ value, onChange, placeholder = 'MChJ nomi yoki shartnoma raqami…' }: {
  value: string
  onChange: (v: string) => void
  placeholder?: string
}) {
  return (
    <div style={{ position: 'relative', display: 'inline-flex', alignItems: 'center' }}>
      <span aria-hidden style={{
        position: 'absolute', left: 9, fontSize: 12, color: 'var(--ink-3)', pointerEvents: 'none',
      }}>⌕</span>
      <input className="inp" value={value} onChange={e => onChange(e.target.value)}
        placeholder={placeholder} aria-label="Qidirish"
        style={{ width: 250, paddingLeft: 24 }} />
      {value && (
        <button onClick={() => onChange('')} aria-label="Tozalash"
          style={{
            position: 'absolute', right: 6, border: 0, background: 'none',
            cursor: 'pointer', color: 'var(--ink-3)', fontSize: 14, lineHeight: 1, padding: 2,
          }}>×</button>
      )}
    </div>
  )
}
