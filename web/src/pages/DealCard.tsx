import { useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { api, dateUz, money, type DealCard, type DocType } from '../lib/api'
import { ErrorBox, Field, Loading, Modal, errText, useAsync, useToast } from '../lib/ui'

/** Cheklist qadami → chiqariladigan hujjat turi. */
const STEP_DOC: Record<string, DocType> = {
  Contract: 'Contract',
  InvoiceForPayment: 'InvoiceForPayment',
  Doverennost: 'Doverennost',
  Ttn: 'Ttn',
  Esf: 'Esf',
}

/** Hujjatni kim chiqaradi — backenddagi qoidaning aynan nusxasi. */
function issuer(kind: string, step: string, contractByUs = true): 'us' | 'them' {
  if (step === 'Doverennost') return kind === 'Purchase' ? 'us' : 'them'
  if (step === 'Contract') return contractByUs ? 'us' : 'them'
  if (step === 'Payment') return kind === 'Sale' ? 'them' : 'us'
  if (step === 'EsfAccepted') return kind === 'Sale' ? 'them' : 'us'
  return kind === 'Sale' ? 'us' : 'them'
}

export default function DealCardPage() {
  const { id } = useParams()
  const nav = useNavigate()
  const toast = useToast()
  const [busy, setBusy] = useState(false)
  const [docForm, setDocForm] = useState<DocType | null>(null)

  const { data: d, error, loading, reload } = useAsync(
    () => api.get<DealCard>(`/api/deals/${id}`), [id])

  if (loading) return <Loading />
  if (error) return <ErrorBox text={error} />
  if (!d) return null

  const nextStep = d.steps.find(s => s.status === 'Pending')
  const editable = d.status === 'Active' || d.status === 'Draft'

  const act = async (fn: () => Promise<unknown>, ok: string) => {
    setBusy(true)
    try { await fn(); toast(ok); reload() }
    catch (e) { toast(errText(e), true) }
    finally { setBusy(false) }
  }

  const completeStep = (step: string) =>
    act(() => api.post(`/api/deals/${d.id}/steps/${step}/complete`, {}), `"${step}" bajarildi deb belgilandi`)

  const createDoc = (type: DocType, extra: Record<string, unknown> = {}) =>
    act(() => api.post('/api/documents', { dealId: d.id, docType: type, ...extra }),
      'Hujjat shakllandi — imzoga tushdi')

  return (
    <>
      <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 14, flexWrap: 'wrap' }}>
        <button className="btn sm" onClick={() => nav('/deals')}>← Ortga</button>
        <div>
          <h2 className="mono" style={{ fontSize: 18 }}>{d.number}</h2>
          <div style={{ fontSize: 12.5, color: 'var(--ink-3)' }}>
            {d.kind === 'Sale' ? 'Sotuv · ' : 'Xarid · '}{d.counterparty.name}
          </div>
        </div>
        <div style={{ marginLeft: 'auto', display: 'flex', gap: 8 }}>
          {d.status === 'Closed' && <span className="pill ok">✓ Yopilgan</span>}
          {d.status === 'Cancelled' && <span className="pill neut">Bekor qilingan</span>}
          {editable && (
            <button className="btn sm danger" disabled={busy}
              onClick={() => {
                const reason = prompt('Bekor qilish sababi:')
                if (reason) act(() => api.post(`/api/deals/${d.id}/cancel`, { reason }), 'Bekor qilindi')
              }}>Bekor qilish</button>
          )}
        </div>
      </div>

      <div className="deal-grid">
        <div className="col">
          <div className="card">
            <div className="card-h">
              <h2>Cheklist</h2>
              <span className="hint">{d.steps.filter(s => s.status === 'Done').length} / {d.steps.length}</span>
            </div>
            <ul className="steps">
              {d.steps.map((s, i) => {
                const done = s.status === 'Done' || s.status === 'Skipped'
                const isNext = nextStep?.step === s.step
                const who = issuer(d.kind, s.step)
                const docType = STEP_DOC[s.step]
                return (
                  <li key={s.step}>
                    <span className={'st-ic ' + (done ? 'done' : s.isOverdue ? 'late' : isNext ? 'next' : '')}>
                      {done ? '✓' : i + 1}
                    </span>
                    <div>
                      <div className="st-t">
                        {s.label}
                        <span className={'who ' + who}>{who === 'us' ? 'biz' : 'ular'}</span>
                        {!s.isRequired && <span className="pill neut">ixtiyoriy</span>}
                      </div>
                      <div className="st-m">
                        {done ? (s.completedAt ? `bajarildi · ${dateUz(s.completedAt)}` : 'bajarildi')
                          : s.isOverdue ? `muddat o'tdi · ${dateUz(s.dueDate)}`
                          : isNext ? `navbatdagi · ${dateUz(s.dueDate)}` : 'kutilmoqda'}
                      </div>
                      {isNext && editable && (
                        <div className="st-act">
                          <button className="btn sm" disabled={busy} onClick={() => completeStep(s.step)}>
                            Bajarildi
                          </button>
                          {who === 'us' && docType && (
                            <button className="btn sm" disabled={busy}
                              onClick={() => {
                                if (docType === 'Doverennost' || docType === 'Ttn') setDocForm(docType)
                                else createDoc(docType)
                              }}>
                              Hujjat chiqarish
                            </button>
                          )}
                        </div>
                      )}
                    </div>
                  </li>
                )
              })}
            </ul>
          </div>

          <div className="card">
            <div className="card-h"><h2>Ma'lumot</h2></div>
            <dl className="kv">
              <dt>Tur</dt><dd>{d.kind === 'Sale' ? 'Sotuv (biz sotamiz)' : 'Xarid (biz olamiz)'}</dd>
              <dt>Kontragent</dt><dd>{d.counterparty.name}</dd>
              <dt>INN</dt><dd className="mono">{d.counterparty.inn ?? '—'}</dd>
              <dt>Summa</dt><dd className="num">{money(d.amountTotal)}</dd>
              <dt>To'langan</dt>
              <dd className="num" style={{ color: d.amountPaid >= d.amountTotal ? 'var(--ok)' : 'var(--warn)' }}>
                {money(d.amountPaid)}</dd>
              <dt>Qolgan qarz</dt>
              <dd className="num" style={{ color: d.amountTotal - d.amountPaid > 0 ? 'var(--bad)' : 'var(--ok)' }}>
                {money(d.amountTotal - d.amountPaid)}</dd>
              <dt>Yosh</dt><dd>{d.ageDays} kun</dd>
              <dt>Shartnoma sanasi</dt><dd>{dateUz(d.contractDate)}</dd>
            </dl>
          </div>
        </div>

        <div className="col">
          <div className="card">
            <div className="card-h"><h2>Tovarlar</h2>
              <span className="hint">{d.items.length} pozitsiya</span></div>
            <div className="tw">
              <table>
                <thead><tr>
                  <th>Tovar</th><th>MXIK</th><th className="r">Soni</th>
                  <th className="r">Narx</th><th className="r">Summa</th>
                </tr></thead>
                <tbody>
                  {d.items.map(it => (
                    <tr key={it.id}>
                      <td>{it.product}</td>
                      <td className="mono" style={{ fontSize: 11.5, color: it.mxik ? 'var(--ink-3)' : 'var(--crit)' }}>
                        {it.mxik ?? "yo'q ⚠"}</td>
                      <td className="r num">{money(it.qty)} {it.unit}</td>
                      <td className="r num">{money(it.price)}</td>
                      <td className="r num" style={{ fontWeight: 500 }}>{money(it.amount)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div style={{ padding: '10px 16px', borderTop: '1px solid var(--line)', textAlign: 'right', fontSize: 13, color: 'var(--ink-3)' }}>
              Jami <b className="num" style={{ color: 'var(--ink)', fontSize: 15, marginLeft: 8 }}>
                {money(d.amountTotal)}</b>
            </div>
          </div>

          <div className="card">
            <div className="card-h">
              <h2>Hujjatlar zanjiri</h2>
              <span className="hint">qaysi hujjat qaysi asos bilan chiqqan</span>
            </div>
            {d.documents.length === 0
              ? <div className="empty">Hali hujjat yo'q</div>
              : <ul className="chain">
                  {d.documents.map(x => {
                    const us = x.direction === 'Out'
                    return (
                      <li key={x.id}>
                        <span className={'ch-ic ' + (us ? 'us' : 'them')} title={us ? 'biz berdik' : 'bizga keldi'}>
                          {us ? '↑' : '↓'}
                        </span>
                        <div>
                          <div className="ch-hd">
                            <span className="ch-nm">{x.label}</span>
                            <span className="ch-no">{x.docNumber ?? '—'}</span>
                            <span className="ch-meta">
                              <span className={'pill ' + docPill(x.status)}>{docLabel(x.status)}</span>
                              <span style={{ fontSize: 11.5, color: 'var(--ink-3)' }}>{dateUz(x.docDate)}</span>
                            </span>
                          </div>
                          <div className="ch-basis">
                            {x.basis
                              ? <>asos: <b>{x.basis}</b></>
                              : <span style={{ fontStyle: 'italic' }}>zanjir boshi</span>}
                          </div>
                        </div>
                      </li>
                    )
                  })}
                </ul>}
          </div>

          <div className="card">
            <div className="card-h">
              <h2>To'lovlar</h2>
              <span className="hint">{d.kind === 'Sale' ? 'klient bizga' : 'biz postavshikka'}</span>
            </div>
            {/* Qisman to'lov normal holat — qancha qolgani doim ko'rinib tursin. */}
            <div className="debt" style={{ borderBottom: '1px solid var(--line)' }}>
              <div className="debt-row">
                <span>Qolgan qarz</span>
                <span className={'num debt-big ' + (d.amountTotal - d.amountPaid > 0 ? 'due' : 'clear')}>
                  {money(d.amountTotal - d.amountPaid)}
                </span>
              </div>
              <div className="split">
                <i className={d.amountPaid >= d.amountTotal ? '' : 'part'}
                  style={{ width: `${Math.min(100, d.amountTotal ? (d.amountPaid / d.amountTotal) * 100 : 0)}%` }} />
              </div>
              <div className="debt-row">
                <span>To'langan <b className="num" style={{ color: 'var(--ink-2)' }}>{money(d.amountPaid)}</b></span>
                <span>Shartnoma <b className="num" style={{ color: 'var(--ink-2)' }}>{money(d.amountTotal)}</b></span>
              </div>
            </div>
            <div className="tw">
              <table>
                <thead><tr><th>Sana</th><th>Bank hujjati</th><th>Maqsad</th><th className="r">Summa</th></tr></thead>
                <tbody>
                  {d.payments.length === 0 && <tr><td colSpan={4} className="empty">To'lov hali kelmagan</td></tr>}
                  {d.payments.map(p => (
                    <tr key={p.id}>
                      <td style={{ color: 'var(--ink-3)' }}>{dateUz(p.payDate)}</td>
                      <td className="mono" style={{ fontSize: 12 }}>{p.bankDocNo ?? '—'}</td>
                      <td style={{ color: 'var(--ink-2)', fontSize: 12 }}>{p.purpose ?? '—'}</td>
                      <td className="r num" style={{ fontWeight: 500 }}>{money(p.amount)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>

      {docForm === 'Doverennost' && (
        <DoverennostForm dealNumber={d.number} busy={busy}
          onClose={() => setDocForm(null)}
          onSubmit={data => { createDoc('Doverennost', { doverennost: data }); setDocForm(null) }} />
      )}
      {docForm === 'Ttn' && (
        <TtnForm busy={busy}
          onClose={() => setDocForm(null)}
          onSubmit={data => { createDoc('Ttn', { ttn: data }); setDocForm(null) }} />
      )}
    </>
  )
}

const docPill = (s: string) =>
  s === 'Accepted' ? 'ok' : s === 'Sent' || s === 'Delivered' ? 'ok'
    : s === 'Rejected' ? 'crit' : s === 'Signed' || s === 'ReadyToSign' ? 'warn' : 'neut'

const docLabel = (s: string) => ({
  Draft: 'Qoralama', ReadyToSign: 'Imzoda', Signed: 'Imzolangan', Sent: 'Yuborildi',
  Delivered: 'Yetkazildi', Accepted: 'Qabul qilindi', Rejected: 'Rad etildi', Cancelled: 'Bekor',
} as Record<string, string>)[s] ?? s

// ── Doverennost formasi (Didox: Agent.Pinfl MAJBURIY) ──
function DoverennostForm({ dealNumber, busy, onClose, onSubmit }: {
  dealNumber: string; busy: boolean; onClose: () => void
  onSubmit: (d: Record<string, unknown>) => void
}) {
  const emps = useAsync(() => api.get<{ id: number; fullName: string; position: string | null; pinfl: string | null; canReceiveGoods: boolean }[]>('/api/employees'), [])
  const ready = (emps.data ?? []).filter(e => e.canReceiveGoods)
  const [employeeId, setEmployeeId] = useState<number | ''>('')
  const [validUntil, setValidUntil] = useState(() => {
    const d = new Date(); d.setDate(d.getDate() + 10)
    return d.toISOString().slice(0, 10)
  })

  return (
    <Modal title="Doverennost chiqarish" onClose={onClose} busy={busy}
      okLabel="Shakllantirish"
      onOk={() => {
        if (!employeeId) return
        onSubmit({ employeeId, validUntil, basisDocument: dealNumber })
      }}>
      <div className="full alert info">
        <div>Didox <b>ishonchli shaxs PINFL</b> ini majburiy talab qiladi.
          Passport ixtiyoriy — qog'oz nusxa uchun saqlanadi.</div>
      </div>
      {ready.length === 0 && (
        <div className="full alert crit">
          <div>PINFL kiritilgan xodim yo'q. <b>Sozlamalar → Xodimlar</b> bo'limiga kiring.</div>
        </div>
      )}
      <Field label="Ishonchli shaxs" required full>
        <select className="inp" value={employeeId} onChange={e => setEmployeeId(Number(e.target.value))}>
          <option value="">— tanlang —</option>
          {ready.map(e => (
            <option key={e.id} value={e.id}>{e.fullName} · {e.position ?? ''} · {e.pinfl}</option>
          ))}
        </select>
      </Field>
      <Field label="Amal qilish muddati" required hint="muddat o'tsa eslatma chiqadi">
        <input className="inp" type="date" value={validUntil} onChange={e => setValidUntil(e.target.value)} />
      </Field>
      <Field label="Asos hujjat">
        <input className="inp" value={dealNumber} readOnly />
      </Field>
    </Modal>
  )
}

// ── TTN formasi ──
function TtnForm({ busy, onClose, onSubmit }: {
  busy: boolean; onClose: () => void; onSubmit: (d: Record<string, unknown>) => void
}) {
  const drivers = useAsync(() => api.get<{ id: number; fullName: string; pinfl: string | null }[]>('/api/employees?driversOnly=true'), [])
  const vehicles = useAsync(() => api.get<{ id: number; model: string; plateNumber: string }[]>('/api/vehicles'), [])
  const [driverId, setDriverId] = useState<number | ''>('')
  const [vehicleId, setVehicleId] = useState<number | ''>('')
  const [loadAddress, setLoad] = useState('')
  const [unloadAddress, setUnload] = useState('')
  const [distanceKm, setKm] = useState('')
  const [weightBrutto, setW] = useState('')

  return (
    <Modal title="TTN chiqarish" onClose={onClose} busy={busy} okLabel="Shakllantirish"
      onOk={() => {
        if (!driverId || !vehicleId || !loadAddress || !unloadAddress) return
        onSubmit({
          driverId, vehicleId, loadAddress, unloadAddress,
          distanceKm: distanceKm ? Number(distanceKm) : null,
          weightBrutto: weightBrutto ? Number(weightBrutto) : null,
        })
      }}>
      <div className="full alert info">
        <div>Didox TTN uchun haydovchi <b>PINFL</b>, mashina <b>davlat raqami</b> va
          <b> yuk ortish/tushirish nuqtalari</b> talab qiladi.</div>
      </div>
      <Field label="Haydovchi" required>
        <select className="inp" value={driverId} onChange={e => setDriverId(Number(e.target.value))}>
          <option value="">— tanlang —</option>
          {(drivers.data ?? []).map(e => <option key={e.id} value={e.id}>{e.fullName} · {e.pinfl}</option>)}
        </select>
      </Field>
      <Field label="Avtomobil" required>
        <select className="inp" value={vehicleId} onChange={e => setVehicleId(Number(e.target.value))}>
          <option value="">— tanlang —</option>
          {(vehicles.data ?? []).map(v => <option key={v.id} value={v.id}>{v.model} · {v.plateNumber}</option>)}
        </select>
      </Field>
      <Field label="Yuk ortish manzili" required full>
        <input className="inp" value={loadAddress} onChange={e => setLoad(e.target.value)} />
      </Field>
      <Field label="Yuk tushirish manzili" required full>
        <input className="inp" value={unloadAddress} onChange={e => setUnload(e.target.value)} />
      </Field>
      <Field label="Masofa, km">
        <input className="inp" type="number" value={distanceKm} onChange={e => setKm(e.target.value)} />
      </Field>
      <Field label="Og'irlik brutto, t">
        <input className="inp" type="number" step="0.001" value={weightBrutto} onChange={e => setW(e.target.value)} />
      </Field>
    </Modal>
  )
}
