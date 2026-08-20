import { useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { api, dateUz, money, type DealCard } from '../lib/api'
import { ErrorBox, Loading, errText, useAsync, useToast } from '../lib/ui'
import { useAuth } from '../lib/auth'

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
  const { me } = useAuth()

  // Muddat o'tganini shu bilan solishtiramiz (YYYY-MM-DD matn taqqoslash
  // sana taqqoslash bilan bir xil natija beradi).
  const bugun = new Date().toISOString().slice(0, 10)
  const [busy, setBusy] = useState(false)

  const { data: d, error, loading, reload } = useAsync(
    () => api.get<DealCard>(`/api/deals/${id}`), [id])

  if (loading) return <Loading />
  if (error) return <ErrorBox text={error} />
  if (!d) return null

// Izoh maydonida ikki xil narsa turadi: tizimning o'z izi
  // («Kiruvchi hujjatdan avtomatik ochildi») va odam yozgani —
  // masalan bekor qilish sababi. Birinchisi ekranga chiqmaydi,
  // ikkinchisi esa hech qayerda ko'rinmasdi.
  const izoh = (d.comment ?? '')
    .split(String.fromCharCode(10))
    .filter(q => q.trim() && !q.startsWith('Kiruvchi hujjatdan avtomatik ochildi'))
    .join(String.fromCharCode(10))
    .trim()

  const nextStep = d.steps.find(s => s.status === 'Pending')
  // Shartnomani bekor qilish — faqat rahbar. Buxgalterning ekrani
  // ko'rish uchun: hujjat harakati Didoxda bo'ladi. Ilgari bu tugma
  // ikkala buxgalterga ham ko'rinardi va ular xarid shartnomasini
  // bekor qila olardi.
  const editable = me?.role === 'Admin' && (d.status === 'Active' || d.status === 'Draft')

  const act = async (fn: () => Promise<unknown>, ok: string) => {
    setBusy(true)
    try { await fn(); toast(ok); reload() }
    catch (e) { toast(errText(e), true) }
    finally { setBusy(false) }
  }

  return (
    <>
      <div style={{ display: 'flex', alignItems: 'center', gap: 12, marginBottom: 14, flexWrap: 'wrap' }}>
        {/* «Shartnomalar» sahifasi olib tashlangan — orqaga qaytish
            brauzer tarixi bo'yicha ishlaydi, aks holda «Sahifa topilmadi»
            chiqardi. */}
        <button className="btn sm" onClick={() => nav(-1)}>← Ortga</button>
        <div>
          <h2 className="mono" style={{ fontSize: 18 }}>{d.number}</h2>
          <div style={{ fontSize: 12.5, color: 'var(--ink-3)' }}>
            {d.kind === 'Sale' ? 'Sotuv · ' : 'Xarid · '}{d.counterparty.name}
          </div>
        </div>
        <div style={{ marginLeft: 'auto', display: 'flex', gap: 8 }}>
          {d.status === 'Closed' && <span className="pill ok">✓ Tugagan</span>}
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
              <h2>Bosqichlar</h2>
              <span className="hint">{d.steps.filter(s => s.status === 'Done').length} / {d.steps.length}</span>
            </div>
            <ul className="steps">
              {d.steps.map((s, i) => {
                const done = s.status === 'Done' || s.status === 'Skipped'
                const isNext = nextStep?.step === s.step
                const who = issuer(d.kind, s.step)
                return (
                  <li key={s.step}>
                    <span className={'st-ic ' + (done ? 'done' : s.isOverdue ? 'late' : isNext ? 'next' : '')}>
                      {done ? '✓' : i + 1}
                    </span>
                    <div>
                      <div className="st-t">
                        {s.label}
                        <span className={'who ' + who}>{who === 'us' ? 'Biz yuboramiz' : 'Ular yuboradi'}</span>
                        {!s.isRequired && <span className="pill neut">majburiy emas</span>}
                      </div>
                      <div className="st-m">
                        {done ? (s.completedAt ? `bajarildi · ${dateUz(s.completedAt)}` : 'bajarildi')
                          : s.isOverdue ? `muddat o'tdi · ${dateUz(s.dueDate)}`
                          : isNext ? `navbatdagi · ${dateUz(s.dueDate)}` : 'kutilmoqda'}
                      </div>
                    </div>
                  </li>
                )
              })}
            </ul>
          </div>

          <div className="card">
            <div className="card-h"><h2>Shartnoma ma'lumoti</h2></div>
            <dl className="kv">
              <dt>Yo'nalish</dt><dd>{d.kind === 'Sale' ? 'Sotuv (biz sotamiz)' : 'Xarid (biz olamiz)'}</dd>
              <dt>MChJ nomi</dt><dd>{d.counterparty.name}</dd>
              <dt>INN</dt><dd className="mono">{d.counterparty.inn ?? '—'}</dd>
              <dt>Shartnoma summasi</dt><dd className="num">{money(d.amountTotal)}</dd>
              {/* «To'langan summa» va «Qolgan qarz» bu yerdan olib tashlandi:
                  o'sha uch raqam pastdagi «To'lovlar» kartochkasida chizig'i
                  bilan birga turardi. Bitta ekranda ikki marta ko'rsatish
                  o'qiyotgan odamni chalg'itadi — qaysi biri to'g'ri deb
                  o'ylashga majbur qiladi. */}
              <dt>Shartnoma sanasi</dt><dd>{dateUz(d.contractDate)}</dd>
              {/* To'lov muddati serverdan kelardi, lekin hech qayerda
                  ko'rsatilmasdi — kartochkaning eng kerakli sanasi. */}
              <dt>To'lov muddati</dt>
              <dd style={{
                color: d.dueDate && d.amountTotal > d.amountPaid && d.dueDate < bugun
                  ? 'var(--crit)' : undefined,
                fontWeight: d.dueDate && d.amountTotal > d.amountPaid && d.dueDate < bugun ? 600 : 400,
              }}>
                {d.dueDate
                  ? dateUz(d.dueDate) +
                    (d.amountTotal > d.amountPaid && d.dueDate < bugun ? " — muddati o'tgan" : '')
                  : "ko'rsatilmagan"}
              </dd>
              <dt>Ochilganiga</dt><dd>{d.ageDays} kun</dd>
              <dt>Mas'ul xodim</dt><dd>{d.manager ?? '—'}</dd>
              {izoh && <>
                <dt>Izoh</dt>
                <dd style={{ whiteSpace: 'pre-line', color: 'var(--crit)' }}>{izoh}</dd>
              </>}
            </dl>
          </div>
        </div>

        <div className="col">
          <div className="card">
            <div className="card-h">
              <h2>Hujjatlar ketma-ketligi</h2>
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
                              : <span style={{ fontStyle: 'italic' }}>birinchi hujjat</span>}
                          </div>
                          {/* Hujjat rad etilgan bo'lsa SABABI ko'rinsin.
                              Sabab serverdan kelardi, lekin ekranda hech
                              qayerda chiqmasdi — buxgalter nega rad
                              etilganini bilmasdi. */}
                          {x.rejectReason &&
                            <div style={{ fontSize: 12, color: 'var(--crit)', marginTop: 3 }}>
                              rad etish sababi: <b>{x.rejectReason}</b>
                            </div>}
                        </div>
                      </li>
                    )
                  })}
                </ul>}
          </div>

          <div className="card">
            <div className="card-h">
              <h2>Shu shartnoma bo'yicha to'lovlar</h2>
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
                <thead><tr><th>To'lov sanasi</th><th>Bank hujjati raqami</th><th>To'lov maqsadi</th><th className="r">To'lov summasi</th></tr></thead>
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
