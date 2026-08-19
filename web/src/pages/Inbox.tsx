import { useNavigate } from 'react-router-dom'
import { api, dateUz, money, type InboxList } from '../lib/api'
import { ErrorBox, Loading, useAsync } from '../lib/ui'

const LABEL: Record<string, string> = {
  Contract: 'Dogovor', InvoiceForPayment: 'Schyot na oplatu', Doverennost: 'Doverennost',
  Ttn: 'TTN', Esf: 'Schyot-faktura', Act: 'Akt',
}

export default function Inbox() {
  const nav = useNavigate()
  const { data, loading, error } = useAsync(() => api.get<InboxList>('/api/inbox'), [])

  if (loading) return <Loading />
  if (error) return <ErrorBox text={error} />
  if (!data) return null

  return (
    <>
      {/* Didox yoqilgan bo'lsa hech qanday izoh kerak emas — hujjatlar
          shunchaki keladi. Faqat YOQILMAGAN holat aytiladi, chunki
          bo'sh ro'yxatning sababi shu. */}
      {!data.didoxEnabled && (
        <div className="alert warn" style={{ marginBottom: 14 }}>
          <div><b>Didox ulanmagan</b> — hujjatlar hali kelmaydi.</div>
        </div>
      )}

      <div className="card">
        <div className="card-h">
          <h2>Kelgan hujjatlar</h2>
          <span className="hint">Didoxdan tushgan tartibda</span>
        </div>
        <div className="tw">
          <table>
            <thead>
              <tr>
                <th>Hujjat turi</th><th>Hujjat raqami</th><th>Kimdan keldi (MChJ)</th><th className="r">Hujjat summasi</th>
                <th>Qaysi shartnomaga</th><th>Hujjat holati</th><th>Hujjat sanasi</th>
              </tr>
            </thead>
            <tbody>
              {data.items.length === 0 && <tr><td colSpan={7} className="empty">Hali hujjat kelmadi</td></tr>}
              {data.items.map(x => (
                <tr key={x.id}>
                  <td>{LABEL[x.type] ?? x.type}</td>
                  <td className="mono" style={{ fontSize: 12 }}>{x.docNumber ?? '—'}</td>
                  <td>{x.sender ?? '—'}</td>
                  <td className="r num">{x.amount ? money(x.amount) : '—'}</td>
                  <td className="mono" style={{ fontSize: 12 }}>
                    {x.dealId
                      ? <a style={{ cursor: 'pointer' }} onClick={() => nav(`/deals/${x.dealId}`)}>{x.deal}</a>
                      : '—'}
                  </td>
                  <td>
                    <span className={'pill ' + (x.status === 'Accepted' ? 'ok' : x.status === 'Rejected' ? 'crit' : 'warn')}>
                      {x.status === 'Accepted' ? 'Qabul qilindi' : x.status === 'Rejected' ? 'Rad etildi' : 'Yangi kelgan'}
                    </span>
                  </td>
                  <td style={{ color: 'var(--ink-3)', fontSize: 12 }}>{dateUz(x.docDate)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

    </>
  )
}
