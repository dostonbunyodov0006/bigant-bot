import { useState } from 'react'
import { api, money, dateUz } from '../lib/api'
import { ErrorBox, Loading, useAsync } from '../lib/ui'
import { Kpi } from '../components/RoleUi'

interface View {
  period: { from: string; to: string; days: number }
  scope: string
  deals: { opened: number; closed: number; stillOpen: number }
  money: {
    paidOutTotal: number; paidOutCount: number
    receivedTotal: number; receivedCount: number
    weOwe: number; theyOwe: number
  }
  documents: { type: string; label: string; sent: number; received: number }[]
  chainHealth: { activeDeals: number; broken: number }
  counterparties: {
    name: string; kind: string; deals: number
    total: number; paid: number; debt: number
  }[]
}

const PERIODS = [
  { d: 30, label: '30 kun' },
  { d: 90, label: '3 oy' },
  { d: 180, label: '6 oy' },
  { d: 365, label: '1 yil' },
]

export default function Hisobot() {
  const [days, setDays] = useState(30)
  const { data, loading, error } = useAsync<View>(
    () => api.get(`/api/view/hisobot?days=${days}`), [days])

  if (error) return <ErrorBox text={error} />

  return (
    <>
      <div className="filters">
        <div className="seg">
          {PERIODS.map(p => (
            <button key={p.d} aria-pressed={days === p.d} onClick={() => setDays(p.d)}>
              {p.label}
            </button>
          ))}
        </div>
        {data && (
          <span style={{ fontSize: 12.5, color: 'var(--ink-3)' }}>
            {dateUz(data.period.from)} — {dateUz(data.period.to)}
            {data.scope === 'Sale' && ' · faqat sotuv'}
          </span>
        )}
      </div>

      {loading || !data ? <Loading /> : (
        <>
          <div className="kpis">
            <Kpi label="Yangi ochilgan shartnoma" value={data.deals.opened} note={`${data.period.days} kun ichida`} />
            <Kpi label="Tugagan shartnoma" value={data.deals.closed} tone="ok" note="hujjatlari to'liq yig'ilgan" />
            <Kpi label="Davom etayotgan shartnoma" value={data.deals.stillOpen} note="hujjatlari hali to'liq emas" />
            <Kpi label="Biz to'lagan pul" value={money(data.money.paidOutTotal)}
              note={`${data.money.paidOutCount} ta to'lov`} />
            <Kpi label="Bizga tushgan pul" value={money(data.money.receivedTotal)} tone="ok"
              note={`${data.money.receivedCount} ta to'lov`} />
          </div>

          <div className="col">
              <div className="card">
                <div className="card-h">
                  <h2>Hujjatlar aylanmasi</h2>
                </div>
                <div className="tw">
                  <table>
                    <thead>
                      <tr>
                        <th>Hujjat turi</th>
                        <th className="r">Biz yuborgan hujjat</th>
                        <th className="r">Bizga kelgan hujjat</th>
                        <th className="r">Jami hujjat</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.documents.map(d => (
                        <tr key={d.type}>
                          <td>{d.label}</td>
                          <td className="r num" style={{ color: d.sent ? 'var(--role)' : 'var(--ink-3)' }}>
                            {d.sent || '—'}
                          </td>
                          <td className="r num" style={{ color: d.received ? 'var(--ink)' : 'var(--ink-3)' }}>
                            {d.received || '—'}
                          </td>
                          <td className="r num" style={{ fontWeight: 600 }}>{d.sent + d.received}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>

              <div className="card">
                <div className="card-h">
                  <h2>Kontragentlar bo'yicha</h2>
                </div>
                <div className="tw">
                  <table>
                    <thead>
                      <tr>
                        <th>MChJ nomi</th><th>Yo'nalish</th><th className="r">Shartnoma soni</th>
                        <th className="r">Umumiy summa</th><th className="r">To'langan summa</th><th className="r">Qolgan qarz</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.counterparties.length === 0 &&
                        <tr><td colSpan={6} className="empty">Bu davrda shartnoma yo'q</td></tr>}
                      {data.counterparties.map((c, i) => (
                        <tr key={c.name + c.kind + i}>
                          <td style={{ fontWeight: 500 }}>{c.name}</td>
                          <td>
                            <span className={'pill ' + (c.kind === 'Sale' ? 'acc' : 'neut')}>
                              {c.kind === 'Sale' ? 'Sotuv' : 'Xarid'}
                            </span>
                          </td>
                          <td className="r num">{c.deals}</td>
                          <td className="r num">{money(c.total)}</td>
                          <td className="r num" style={{ color: 'var(--ok)' }}>{money(c.paid)}</td>
                          <td className="r num" style={{
                            color: c.debt > 0 ? 'var(--crit)' : 'var(--ok)',
                            fontWeight: c.debt > 0 ? 600 : 400,
                          }}>{money(c.debt)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
          </div>

          <div className="grid2even">
              <div className="card">
                <div className="card-h"><h2>Qarzlar holati</h2></div>
                <dl className="kv">
                  <dt>Biz postavshiklarga qarzdormiz</dt>
                  <dd className="num" style={{ color: 'var(--crit)', fontWeight: 600 }}>
                    {money(data.money.weOwe)}
                  </dd>
                  <dt>Klientlar bizga qarzdor</dt>
                  <dd className="num" style={{ color: 'var(--ok)', fontWeight: 600 }}>
                    {money(data.money.theyOwe)}
                  </dd>
                  <dt>Farq (bizning foydamizga)</dt>
                  <dd className="num">{money(data.money.theyOwe - data.money.weOwe)}</dd>
                </dl>
              </div>

              <div className="card">
                <div className="card-h"><h2>Hujjatlari to'liq shartnomalar ulushi</h2></div>
                <div style={{ padding: '14px 16px' }}>
                  <div style={{ fontSize: 13, color: 'var(--ink-2)', marginBottom: 10 }}>
                    Davom etayotgan shartnomalarda hujjatlar to'g'ri bog'langanmi
                  </div>
                  <div className="debt-row">
                    <span>Tekshirilgan</span>
                    <span className="num">{data.chainHealth.activeDeals}</span>
                  </div>
                  <div className="split" style={{ margin: '8px 0' }}>
                    <i className={data.chainHealth.broken === 0 ? '' : 'part'}
                      style={{
                        width: `${data.chainHealth.activeDeals
                          ? ((data.chainHealth.activeDeals - data.chainHealth.broken)
                             / data.chainHealth.activeDeals) * 100 : 100}%`,
                      }} />
                  </div>
                  <div className="debt-row">
                    <span>Uzilgan</span>
                    <span className="num" style={{
                      color: data.chainHealth.broken > 0 ? 'var(--crit)' : 'var(--ok)',
                      fontWeight: 600,
                    }}>{data.chainHealth.broken}</span>
                  </div>
                </div>
              </div>
          </div>
        </>
      )}
    </>
  )
}
