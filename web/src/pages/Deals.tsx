import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, money, type DealRow } from '../lib/api'
import { ErrorBox, Loading, Seg, useAsync } from '../lib/ui'
import { fold } from '../components/TableSort'

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

  // Qidiruv bot bilan bir xil qoidada: katta-kichik harf va x/h farqi yo'q.
  const rows = useMemo(() => {
    const n = fold(q)
    return (data ?? []).filter(d =>
      !n || fold(d.number).includes(n) || fold(d.counterparty).includes(n))
  }, [data, q])

  // Filtrga tushgan bitimlar bo'yicha jami qarz — bir qarashda ko'rinsin.
  const debt = rows.reduce((a, d) => a + Math.max(0, d.amountTotal - d.amountPaid), 0)

  return (
    <>
      <div className="filters">
        <Seg value={kind} onChange={setKind} options={[
          { v: 'all', label: 'Sotuv va xarid' },
          { v: 'Sale', label: '→ Sotuv' },
          { v: 'Purchase', label: '← Xarid' },
        ]} />
        <Seg value={status} onChange={setStatus} options={[
          { v: 'Active', label: 'Davom etayotgan shartnomalar' },
          { v: 'Closed', label: 'Tugagan shartnomalar' },
          { v: 'all', label: 'Barcha shartnomalar' },
        ]} />
        <input className="inp" style={{ width: 230 }} placeholder="MChJ nomi yoki shartnoma raqami…"
          value={q} onChange={e => setQ(e.target.value)} />
        <span style={{ fontSize: 12.5, color: 'var(--ink-3)' }}>
          {rows.length} ta shartnoma
          {debt > 0 && <> · qarz <b className="num" style={{ color: 'var(--crit)' }}>{money(debt)}</b></>}
        </span>
      </div>

      <div className="card">
        {loading && <Loading />}
        {error && <ErrorBox text={error} />}
        {!loading && !error && (
          <div className="tw">
            <table>
              <thead>
                <tr>
                  <th>Shartnoma raqami</th><th>Yo'nalish</th><th>MChJ nomi</th>
                  <th className="r">Shartnoma summasi</th><th className="r">To'langan summa</th><th className="r">Qolgan qarz</th><th>Bajarilgan bosqich</th><th>Shartnoma holati</th>
                </tr>
              </thead>
              <tbody>
                {rows.length === 0 && <tr><td colSpan={8} className="empty">Shartnoma topilmadi</td></tr>}
                {rows.map(d => {
                  const unpaid = d.amountTotal - d.amountPaid
                  const pct = d.stepsTotal ? (d.stepsDone / d.stepsTotal) * 100 : 0
                  return (
                    <tr key={d.id} className="click" tabIndex={0}
                      onClick={() => nav(`/deals/${d.id}`)}
                      onKeyDown={e => { if (e.key === 'Enter') nav(`/deals/${d.id}`) }}>
                      <td className="mono" style={{ fontWeight: 500 }}>{d.number}</td>
                      <td><span className={'pill ' + (d.kind === 'Sale' ? 'acc' : 'buy')}>
                        {d.kind === 'Sale' ? '→ Sotuv' : '← Xarid'}</span></td>
                      <td>{d.counterparty}</td>
                      <td className="r num">{money(d.amountTotal)}</td>
                      <td className="r num" style={{ color: d.amountPaid > 0 ? 'var(--ok)' : 'var(--ink-3)' }}>{money(d.amountPaid)}</td>
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
                          {d.status === 'Closed' ? 'Tugagan' : d.status === 'Cancelled' ? 'Bekor qilingan' : d.status === 'Draft' ? 'Qoralama' : 'Davom etyapti'}
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
