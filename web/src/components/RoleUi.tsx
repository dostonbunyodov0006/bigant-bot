import type { ReactNode } from 'react'

/**
 * «BU EKRAN NIMA UCHUN» tasmasi.
 *
 * Har rol ekranining eng tepasida turadi. Maqsad: foydalanuvchi birinchi
 * kirganda ham nima ko'rayotganini va nima qila olishini o'qib tushunsin —
 * hech kimdan so'rashi shart bo'lmasin.
 */
export function Explain({ mark, title, children, can }: {
  mark: string
  title: string
  children: ReactNode
  can: string[]
}) {
  return (
    <div className="explain">
      <div className="mark">{mark}</div>
      <div>
        <h2>{title}</h2>
        <p>{children}</p>
        <div className="can">
          {can.map(c => <span key={c}>{c}</span>)}
        </div>
      </div>
    </div>
  )
}

/** Yirik ko'rsatkich. tone — semantik rang, aksentdan alohida. */
export function Kpi({ label, value, note, tone }: {
  label: string
  value: string | number
  note?: string
  tone?: 'ok' | 'warn' | 'crit'
}) {
  return (
    <div className={'kpi' + (tone ? ' ' + tone : '')}>
      <div className="k">{label}</div>
      <div className="v">{value}</div>
      {note && <div className="m">{note}</div>}
    </div>
  )
}

/**
 * «Sizning ishingiz» — bajarilishi kerak bo'lgan aniq ish.
 * Bo'sh bo'lsa yashil tasdiq chiqadi: ish yo'q degani ham xabar.
 */
export function Work({ title, empty, items }: {
  title: string
  empty: string
  items: { key: string; text: string; sub?: string; right?: ReactNode; tone?: 'crit' | 'ok' }[]
}) {
  return (
    <div className="card" style={{ marginBottom: 16 }}>
      <div className="card-h">
        <h2>{title}</h2>
        {items.length > 0 && <span className="pill warn">{items.length} ta ish</span>}
      </div>
      <div style={{ padding: 14 }}>
        {items.length === 0
          ? <div className="work-empty">✓ {empty}</div>
          : <div className="work">
              {items.map(i => (
                <div key={i.key} className={'work-item' + (i.tone ? ' ' + i.tone : '')}>
                  <div>
                    <div className="t">{i.text}</div>
                    {i.sub && <div className="s">{i.sub}</div>}
                  </div>
                  {i.right && <div className="num" style={{ fontWeight: 600 }}>{i.right}</div>}
                </div>
              ))}
            </div>}
      </div>
    </div>
  )
}
