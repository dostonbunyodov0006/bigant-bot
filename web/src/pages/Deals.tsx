import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, money, type DealRow } from '../lib/api'
import { ErrorBox, Loading, Seg, useAsync } from '../lib/ui'

type KindF = 'all' | 'Sale' | 'Purchase'
type StatusF = 'Active' | 'Closed' | 'all'

export default function Deals() {
  const nav = useNavigate()
  const [kind, setKind] = useState<KindF>('all')
  const [status, setStatus] = useState<StatusF>('Active')
  const [q, setQ] = useState('')

  const { data, loading, error } = useAsync(
    () => api.get<DealRow[]>(`/api/deals?take=300${kind !== 'all' ? `&kind=${kind}` : ''}${status !== 'all' ? `&status=${status}` : ''}`),
    [kind, status],
  )

  const rows = useMemo(() => {
    const s = q.trim().toLowerCase()
    return (data ?? []).filter(d =>
      !s || d.number.toLowerCase().includes(s) || d.counterparty.toLowerCase().includes(s))
  }, [data, q])

  // Filtrga tushgan bitimlar bo'yicha jami qarz — bir qarashda ko'rinsin.
  const debt = rows.reduce((a, d) => a + Math.max(0, d.amountTotal - d.amountPaid), 0)

  return (
    <>
      <div className="filters">
        <Seg value={kind} onChange={setKind} options={[
          { v: 'all', label: 'Hammasi' },
          { v: 'Sale', label: 'Sotuv' },
          { v: 'Purchase', label: 'Xarid' },
        ]} />
        <Seg value={status} onChange={setStatus} options={[
          { v: 'Active', label: 'Ochiq' },
          { v: 'Closed', label: 'Yopilgan' },
          { v: 'all', label: 'Barchasi' },
        ]} />
        <input className="inp" style={{ width: 230 }} placeholder="Raqam yoki kontragent…"
          value={q} onChange={e => setQ(e.target.value)} />
        <span style={{ fontSize: 12.5, color: 'var(--ink-3)' }}>
          {rows.length} ta
          {debt > 0 && <> · qarz <b className="num" style={{ color: 'var(--crit)' }}>{money(debt)}</b></>}
        </span>
        <button className="btn pri" style={{ marginLeft: 'auto' }} onClick={() => nav('/deals/new')}>
          + Yangi shartnoma
        </button>
      </div>

      <div className="card">
        {loading && <Loading />}
        {error && <ErrorBox text={error} />}
        {!loading && !error && (
          <div className="tw">
            <table>
              <thead>
                <tr>
                  <th>Raqam</th><th>Tur</th><th>Kontragent</th>
                  <th className="r">Summa</th><th className="r">Qarz</th><th>Cheklist</th><th>Holat</th>
                </tr>
              </thead>
              <tbody>
                {rows.length === 0 && <tr><td colSpan={7} className="empty">Shartnoma topilmadi</td></tr>}
                {rows.map(d => {
                  const unpaid = d.amountTotal - d.amountPaid
                  const pct = d.stepsTotal ? (d.stepsDone / d.stepsTotal) * 100 : 0
                  return (
                    <tr key={d.id} className="click" tabIndex={0}
                      onClick={() => nav(`/deals/${d.id}`)}
                      onKeyDown={e => { if (e.key === 'Enter') nav(`/deals/${d.id}`) }}>
                      <td className="mono" style={{ fontWeight: 500 }}>{d.number}</td>
                      <td><span className={'pill ' + (d.kind === 'Sale' ? 'acc' : 'neut')}>
                        {d.kind === 'Sale' ? 'Sotuv' : 'Xarid'}</span></td>
                      <td>{d.counterparty}</td>
                      <td className="r num">{money(d.amountTotal)}</td>
                      <td className="r num" style={{ color: unpaid > 0 ? 'var(--crit)' : 'var(--ok)', fontWeight: unpaid > 0 ? 600 : 400 }}>
                        {money(unpaid)}</td>
                      <td style={{ minWidth: 126 }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                          <div className="bar-track" style={{ width: 62 }}>
                            <div className={'bar-fill' + (pct === 100 ? ' ok' : '')} style={{ width: `${pct}%` }} />
                          </div>
                          <span className="num" style={{ fontSize: 12, color: 'var(--ink-3)' }}>
                            {d.stepsDone}/{d.stepsTotal}</span>
                        </div>
                      </td>
                      <td>
                        <span className={'pill ' + (d.status === 'Closed' ? 'ok' : d.status === 'Cancelled' ? 'neut' : 'warn')}>
                          {d.status === 'Closed' ? 'Yopildi' : d.status === 'Cancelled' ? 'Bekor' : d.status === 'Draft' ? 'Qoralama' : 'Ochiq'}
                        </span>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </>
  )
}
