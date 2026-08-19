import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, money, dateTimeUz } from '../lib/api'
import { ErrorBox, Loading, useAsync } from '../lib/ui'
import { useLive } from '../lib/live'
import { Kpi } from '../components/RoleUi'
import { fold, Search, StatusFilter, useSort } from '../components/TableSort'

interface View {
  byKind: { kind: string; count: number; total: number; paid: number; debt: number }[]
  users: { id: number; username: string; fullName: string; role: string }[]
  recent: {
    id: number; number: string; kind: string; counterparty: string
    amountTotal: number; amountPaid: number; debt: number; status: string
    createdAt: string; stepsDone: number; stepsTotal: number
  }[]
  counts: {
    counterparties: number; documents: number
    openDeals: number; closedDeals: number
  }
}

const ROLE_UZ: Record<string, string> = {
  Realizatsiya: 'Realizatsiya buxgalteri',
  Perehod: 'Perehod buxgalteri',
  Admin: 'Administrator',
}

export default function AdminView() {
  const nav = useNavigate()
  const { data, loading, error, reload } = useAsync<View>(
    () => api.get('/api/view/admin'), [], 60000)

  // Ikkala yo'nalish bitta ro'yxatda turadi — qidiruv va filtr busiz
  // bitimni topib bo'lmaydi.
  const [q, setQ] = useState('')
  const [filter, setFilter] = useState<'open' | 'closed' | 'all'>('all')
  const [kind, setKind] = useState<'all' | 'Sale' | 'Purchase'>('all')

  const all = data?.recent ?? []
  const needle = fold(q)
  const shown = all
    .filter(d => kind === 'all' || d.kind === kind)
    .filter(d => filter === 'all' ? true
      : filter === 'closed' ? d.status === 'Closed' : d.status !== 'Closed')
    .filter(d => !needle
      || fold(d.counterparty).includes(needle)
      || fold(d.number).includes(needle))

  const { rows: recent, th, mark } = useSort(shown, 'debt')

  // Jonli oqim: server yangi xabar chiqarishi bilan ekran yangilanadi.
  // 60 soniyalik so'rov — zaxira, aloqa uzilib qolgan holat uchun.
  useLive(() => reload())

  if (loading) return <Loading />
  if (error) return <ErrorBox text={error} />
  if (!data) return null

  const sale = data.byKind.find(k => k.kind === 'Sale')
  const buy = data.byKind.find(k => k.kind === 'Purchase')

  return (
    <>
      <div className="kpis">
        <Kpi label="Davom etayotgan sotuv" value={sale?.count ?? 0}
          note={money(sale?.total ?? 0) + " so'm"} />
        <Kpi label="Klientlar bizga qarzdor" value={money(sale?.debt ?? 0)}
          tone={(sale?.debt ?? 0) > 0 ? 'warn' : 'ok'} note="klientlardan olinishi kerak" />
        <Kpi label="Davom etayotgan xarid" value={buy?.count ?? 0}
          note={money(buy?.total ?? 0) + " so'm"} />
        <Kpi label="Biz postavshiklarga qarzdormiz" value={money(buy?.debt ?? 0)}
          tone={(buy?.debt ?? 0) > 0 ? 'crit' : 'ok'} note="postavshiklarga to'lanishi kerak" />
        <Kpi label="Tugagan shartnomalar" value={data.counts.closedDeals} tone="ok"
          note="hujjatlari to'liq yig'ilgan" />
      </div>

      <div className="card" style={{ marginBottom: 16 }}>
          <div className="card-h">
            <h2>Barcha shartnomalar</h2>
            <div className="r">
              <Search value={q} onChange={setQ} />
              <div className="seg">
                {([['all', 'Ikkalasi'], ['Sale', 'Sotuv'], ['Purchase', 'Xarid']] as const).map(([v, l]) => (
                  <button key={v} aria-pressed={kind === v} onClick={() => setKind(v)}>{l}</button>
                ))}
              </div>
              <StatusFilter value={filter} onChange={setFilter} counts={{
                open: all.filter(d => d.status !== 'Closed').length,
                closed: all.filter(d => d.status === 'Closed').length,
              }} />
            </div>
          </div>
          <div className="tw">
            <table>
              <thead>
                <tr>
                  <th {...th('number')}>Shartnoma raqami{mark('number')}</th>
                  <th {...th('kind')}>Yo'nalish{mark('kind')}</th>
                  <th {...th('counterparty')}>MChJ nomi{mark('counterparty')}</th>
                  <th {...th('amountTotal')} className={th('amountTotal').className + ' r'}>Shartnoma summasi{mark('amountTotal')}</th>
                  <th {...th('amountPaid')} className={th('amountPaid').className + ' r'}>To'langan summa{mark('amountPaid')}</th>
                  <th {...th('debt')} className={th('debt').className + ' r'}>Qolgan qarz{mark('debt')}</th>
                  <th>Bajarilgan bosqich</th>
                  <th {...th('status')}>Shartnoma holati{mark('status')}</th>
                </tr>
              </thead>
              <tbody>
                {recent.length === 0 && (
                  <tr><td colSpan={8} className="empty">
                    {q ? `«${q}» topilmadi` : 'Shartnoma yo\'q'}
                  </td></tr>
                )}
                {recent.map(d => {
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
                      <td className="r num" style={{ color: d.amountPaid > 0 ? 'var(--ok)' : 'var(--ink-3)' }}>
                        {money(d.amountPaid)}</td>
                      <td className="r num" style={{
                        color: d.debt > 0 ? 'var(--crit)' : 'var(--ok)',
                        fontWeight: d.debt > 0 ? 600 : 400,
                      }}>
                        {money(d.debt)}</td>
                      <td style={{ minWidth: 110 }}>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                          <div className="bar-track" style={{ width: 54 }}>
                            <div className={'bar-fill' + (pct === 100 ? ' ok' : '')}
                              style={{ width: `${pct}%` }} />
                          </div>
                          <span className="num" style={{ fontSize: 11.5, color: 'var(--ink-3)' }}>
                            {d.stepsDone}/{d.stepsTotal}</span>
                        </div>
                      </td>
                      <td>
                        <span className={'pill ' + (d.status === 'Closed' ? 'ok'
                          : d.status === 'Cancelled' ? 'neut' : 'warn')}>
                          {d.status === 'Closed' ? 'Tugagan'
                            : d.status === 'Cancelled' ? 'Bekor qilingan'
                            : d.status === 'Draft' ? 'Qoralama' : 'Davom etyapti'}
                        </span>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
      </div>

      <div className="grid3">
          <div className="card">
            <div className="card-h"><h2>Bazadagi umumiy son</h2></div>
            <dl className="kv">
              <dt>Kontragentlar soni</dt><dd className="num">{data.counts.counterparties}</dd>
              <dt>Hujjatlar soni</dt><dd className="num">{data.counts.documents}</dd>
              <dt>Davom etayotgan shartnoma</dt><dd className="num">{data.counts.openDeals}</dd>
              <dt>Tugagan shartnoma</dt><dd className="num">{data.counts.closedDeals}</dd>
            </dl>
          </div>

          <div className="card">
            <div className="card-h"><h2>Oxirgi tushgan hujjat</h2></div>
            <div style={{ padding: '12px 16px', fontSize: 12.5, color: 'var(--ink-3)' }}>
              {data.recent[0]
                ? <>Eng oxirgi shartnoma <b className="mono" style={{ color: 'var(--ink)' }}>
                    {data.recent[0].number}</b> — {dateTimeUz(data.recent[0].createdAt)}</>
                : 'Hali shartnoma yo\'q'}
            </div>
          </div>
          <div className="card">
            <div className="card-h"><h2>Tizim foydalanuvchilari</h2></div>
            <div style={{ padding: '12px 16px', display: 'flex', flexDirection: 'column', gap: 11 }}>
              {data.users.map(u => (
                <div key={u.id} style={{ display: 'flex', gap: 10, alignItems: 'center' }}>
                  <div className="avatar">{u.fullName.split(' ').map(x => x[0]).slice(0, 2).join('')}</div>
                  <div style={{ minWidth: 0 }}>
                    <div style={{ fontSize: 13, fontWeight: 500 }}>{u.fullName}</div>
                    <div style={{ fontSize: 11.5, color: 'var(--ink-3)' }}>
                      {ROLE_UZ[u.role] ?? u.role} · <span className="mono">{u.username}</span>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          </div>
      </div>
    </>
  )
}
