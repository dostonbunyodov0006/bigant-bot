import { useNavigate } from 'react-router-dom'
import { api, money, dateTimeUz } from '../lib/api'
import { ErrorBox, Loading, useAsync } from '../lib/ui'
import { Kpi } from '../components/RoleUi'

interface View {
  byKind: { kind: string; count: number; total: number; paid: number; debt: number }[]
  users: { id: number; username: string; fullName: string; role: string }[]
  recent: {
    id: number; number: string; kind: string; counterparty: string
    amountTotal: number; amountPaid: number; debt: number; status: string
    createdAt: string; stepsDone: number; stepsTotal: number
  }[]
  counts: {
    counterparties: number; products: number; documents: number
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
  const { data, loading, error } = useAsync<View>(() => api.get('/api/view/admin'), [])

  if (loading) return <Loading />
  if (error) return <ErrorBox text={error} />
  if (!data) return null

  const sale = data.byKind.find(k => k.kind === 'Sale')
  const buy = data.byKind.find(k => k.kind === 'Purchase')

  return (
    <>
      <div className="kpis">
        <Kpi label="Sotuv — ochiq" value={sale?.count ?? 0}
          note={money(sale?.total ?? 0) + " so'm"} />
        <Kpi label="Bizga qarzdor" value={money(sale?.debt ?? 0)}
          tone={(sale?.debt ?? 0) > 0 ? 'warn' : 'ok'} note="klientlardan olinadi" />
        <Kpi label="Xarid — ochiq" value={buy?.count ?? 0}
          note={money(buy?.total ?? 0) + " so'm"} />
        <Kpi label="Biz qarzdormiz" value={money(buy?.debt ?? 0)}
          tone={(buy?.debt ?? 0) > 0 ? 'crit' : 'ok'} note="postavshiklarga to'lanadi" />
        <Kpi label="Yopilgan bitim" value={data.counts.closedDeals} tone="ok"
          note="hujjatlari to'liq" />
      </div>

      <div className="grid2">
        <div className="card">
          <div className="card-h">
            <h2>Oxirgi bitimlar</h2>
            <span className="hint">ikkala yo'nalish</span>
          </div>
          <div className="tw">
            <table>
              <thead>
                <tr>
                  <th>Raqam</th><th>Tur</th><th>Kontragent</th>
                  <th className="r">Summa</th><th className="r">Qarz</th>
                  <th>Cheklist</th><th>Holat</th>
                </tr>
              </thead>
              <tbody>
                {data.recent.map(d => {
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
                      <td className="r num" style={{ color: d.debt > 0 ? 'var(--crit)' : 'var(--ok)' }}>
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
                          {d.status === 'Closed' ? 'Yopildi'
                            : d.status === 'Cancelled' ? 'Bekor'
                            : d.status === 'Draft' ? 'Qoralama' : 'Ochiq'}
                        </span>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        </div>

        <div className="col">
          <div className="card">
            <div className="card-h"><h2>Foydalanuvchilar</h2></div>
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

          <div className="card">
            <div className="card-h"><h2>Bazada</h2></div>
            <dl className="kv">
              <dt>Kontragentlar</dt><dd className="num">{data.counts.counterparties}</dd>
              <dt>Tovarlar</dt><dd className="num">{data.counts.products}</dd>
              <dt>Hujjatlar</dt><dd className="num">{data.counts.documents}</dd>
              <dt>Ochiq bitim</dt><dd className="num">{data.counts.openDeals}</dd>
              <dt>Yopilgan</dt><dd className="num">{data.counts.closedDeals}</dd>
            </dl>
          </div>

          <div className="card">
            <div className="card-h"><h2>Oxirgi harakat</h2></div>
            <div style={{ padding: '12px 16px', fontSize: 12.5, color: 'var(--ink-3)' }}>
              {data.recent[0]
                ? <>Eng oxirgi bitim <b className="mono" style={{ color: 'var(--ink)' }}>
                    {data.recent[0].number}</b> — {dateTimeUz(data.recent[0].createdAt)}</>
                : 'Hali bitim yo\'q'}
            </div>
          </div>
        </div>
      </div>
    </>
  )
}
