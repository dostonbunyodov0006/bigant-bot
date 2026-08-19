import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, dateUz, money, type InboxList } from '../lib/api'
import { ErrorBox, Field, Loading, Modal, errText, useAsync, useToast } from '../lib/ui'

const LABEL: Record<string, string> = {
  Contract: 'Dogovor', InvoiceForPayment: 'Schyot na oplatu', Doverennost: 'Doverennost',
  Ttn: 'TTN', Esf: 'Schyot-faktura', Act: 'Akt',
}

export default function Inbox() {
  const nav = useNavigate()
  const toast = useToast()
  const [busy, setBusy] = useState(false)
  const [form, setForm] = useState(false)
  const { data, loading, error, reload } = useAsync(() => api.get<InboxList>('/api/inbox'), [])

  if (loading) return <Loading />
  if (error) return <ErrorBox text={error} />
  if (!data) return null

  const respond = async (id: number, accept: boolean) => {
    setBusy(true)
    try {
      await api.post(`/api/inbox/${id}/respond?accept=${accept}`, {})
      toast(accept ? 'Qabul qilindi · cheklist yangilandi' : 'Rad etildi')
      reload()
    } catch (e) {
      toast(errText(e), true)
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <div className={'alert ' + (data.didoxEnabled ? 'info' : 'warn')} style={{ marginBottom: 14 }}>
        <div>
          {data.didoxEnabled ? (
            <><b>Didox yoqilgan.</b> Kiruvchi hujjatlar har 10 daqiqada o'zi tortiladi va shartnomaga ilinadi.</>
          ) : (
            <><b>Didox o'chiq.</b> Kiruvchi hujjatni qo'lda kiritasiz. Shartnomaga ilinish,
              cheklist va eslatma baribir avtomatik ishlaydi.</>
          )}
        </div>
      </div>

      <div className="card">
        <div className="card-h">
          <h2>Kiruvchi hujjatlar</h2>
          <span className="hint">postavshikdan · klient doverennosti</span>
          <div className="r">
            <button className="btn sm" onClick={() => setForm(true)}>+ Qo'lda kiritish</button>
          </div>
        </div>
        <div className="tw">
          <table>
            <thead>
              <tr>
                <th>Hujjat</th><th>Raqam</th><th>Kimdan</th><th className="r">Summa</th>
                <th>Shartnoma</th><th>Holat</th><th>Sana</th><th />
              </tr>
            </thead>
            <tbody>
              {data.items.length === 0 && <tr><td colSpan={8} className="empty">Kiruvchi hujjat yo'q</td></tr>}
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
                      {x.status === 'Accepted' ? 'Qabul qilindi' : x.status === 'Rejected' ? 'Rad etildi' : 'Yangi'}
                    </span>
                  </td>
                  <td style={{ color: 'var(--ink-3)', fontSize: 12 }}>{dateUz(x.docDate)}</td>
                  <td className="r">
                    {x.status !== 'Accepted' && x.status !== 'Rejected' && (
                      <div style={{ display: 'flex', gap: 6, justifyContent: 'flex-end' }}>
                        <button className="btn sm" disabled={busy} onClick={() => respond(x.id, true)}>Qabul</button>
                        <button className="btn sm danger" disabled={busy} onClick={() => respond(x.id, false)}>Rad</button>
                      </div>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {form && <ManualForm onClose={() => setForm(false)} onDone={() => { setForm(false); reload() }} />}
    </>
  )
}

function ManualForm({ onClose, onDone }: { onClose: () => void; onDone: () => void }) {
  const toast = useToast()
  const [busy, setBusy] = useState(false)
  const [docType, setDocType] = useState('Esf')
  const [docNumber, setNumber] = useState('')
  const [senderInn, setInn] = useState('')
  const [senderName, setName] = useState('')
  const [amount, setAmount] = useState('')
  const [docDate, setDate] = useState(new Date().toISOString().slice(0, 10))

  const save = async () => {
    setBusy(true)
    try {
      const amt = Number(amount) || 0
      await api.post('/api/inbox', {
        docType, docNumber, docDate,
        senderInn: senderInn || null, senderName: senderName || null,
        amount: amt, vatAmount: Math.round((amt * 12) / 112), dealId: null,
      })
      toast('Kiruvchi hujjat ro\'yxatga olindi')
      onDone()
    } catch (e) {
      toast(errText(e), true)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal title="Kiruvchi hujjatni qo'lda kiritish" onClose={onClose} onOk={save}
      busy={busy} okLabel="Ro'yxatga olish">
      <Field label="Hujjat turi" required>
        <select className="inp" value={docType} onChange={e => setDocType(e.target.value)}>
          <option value="Contract">Dogovor</option>
          <option value="Esf">Schyot-faktura</option>
          <option value="Ttn">TTN</option>
          <option value="Doverennost">Doverennost</option>
          <option value="Act">Akt</option>
        </select>
      </Field>
      <Field label="Hujjat raqami" required>
        <input className="inp" value={docNumber} onChange={e => setNumber(e.target.value)} />
      </Field>
      <Field label="Yuboruvchi INN" hint="topilmasa kontragent avtomatik ochiladi">
        <input className="inp mono" value={senderInn} onChange={e => setInn(e.target.value)} maxLength={9} />
      </Field>
      <Field label="Yuboruvchi nomi">
        <input className="inp" value={senderName} onChange={e => setName(e.target.value)} />
      </Field>
      <Field label="Summa (QQS bilan)">
        <input className="inp num" type="number" value={amount} onChange={e => setAmount(e.target.value)} />
      </Field>
      <Field label="Hujjat sanasi" required>
        <input className="inp" type="date" value={docDate} onChange={e => setDate(e.target.value)} />
      </Field>
    </Modal>
  )
}
