import { useEffect, useState } from 'react'
import { api, type Readiness } from '../lib/api'
import { ErrorBox, Field, Loading, errText, useAsync, useToast } from '../lib/ui'

/**
 * SOZLAMALAR — bir martalik tizim sozlamasi.
 *
 * Bu yerda ISH MA'LUMOTI kiritilmaydi: dogovor, doverennost, TTN, faktura
 * va to'lovlar Didox'da bo'ladi, dashboard ularni faqat ko'rsatadi.
 *
 * Ilgari bu sahifada 13 ta rekvizit maydoni, xodimlar va avtomobillar
 * ro'yxati bor edi — ular BIZ hujjat blankalarini to'ldirgan davrdan
 * qolgan edi (doverennostga ishonchli shaxs, TTN ga haydovchi). Kodda
 * ularning birortasi ham o'qilmasdi.
 *
 * Qolgani: tizim holati va o'z INN imiz — hujjat bizga kelganmi yoki biz
 * yuborganmizmi, shu bilan ajratiladi.
 */
interface Company {
  exists: boolean
  name?: string
  tin?: string
  taxpayerType?: number
  missing: string[]
}

export default function Setup() {
  const toast = useToast()
  const ready = useAsync(() => api.get<Readiness>('/api/didox-readiness'), [])
  const co = useAsync(() => api.get<Company>('/api/company'), [])

  const [name, setName] = useState('')
  const [tin, setTin] = useState('')
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!co.data) return
    setName(co.data.name ?? '')
    setTin(co.data.tin ?? '')
  }, [co.data])

  if (co.loading) return <Loading />
  if (co.error) return <ErrorBox text={co.error} />

  const save = async () => {
    setBusy(true)
    try {
      await api.post('/api/company', { name, tin, taxpayerType: co.data?.taxpayerType ?? 10 })
      toast('Saqlandi')
      co.reload(); ready.reload()
    } catch (e) {
      toast(errText(e), true)
    } finally {
      setBusy(false)
    }
  }

  const r = ready.data

  // To'siq — Didox yoqilgach ISHLASHGA HALAQIT BERADIGAN narsa.
  const blockers = r
    ? (r.company.ready ? 0 : 1) + r.counterparties.noInn
      + (r.didox.enabled && !r.didox.hasToken && !r.didox.hasLogin ? 1 : 0)
    : 0

  return (
    <>
      <div className="card" style={{ marginBottom: 16 }}>
        <div className="card-h">
          <h2>Tizim holati</h2>
          <span className="hint">Didox yoqilishidan oldin shular kerak</span>
          <div className="r">
            <span className={'pill ' + (blockers ? 'crit' : 'ok')}>
              {blockers ? `${blockers} ta to'siq` : 'Tayyor'}
            </span>
          </div>
        </div>
        <div style={{ padding: '14px 16px' }}>
          {r && (
            <>
              <Chk ok={r.company.ready}
                text={r.company.ready
                  ? <><b>Bigant INN</b> — {r.company.inn}</>
                  : <><b>Bigant INN</b> kiritilmagan. Busiz hujjat bizga kelganmi
                    yoki biz yuborganmizmi — ajratib bo'lmaydi.</>} />

              <Chk ok={r.didox.enabled}
                text={r.didox.enabled
                  ? <><b>Didox</b> yoqilgan — {r.didox.baseUrl}</>
                  : <><b>Didox</b> hali yoqilmagan. Hujjatlar hali kelmaydi.</>} />

              {r.didox.enabled && (
                <Chk ok={r.didox.hasToken || r.didox.hasLogin}
                  text={r.didox.hasToken || r.didox.hasLogin
                    ? <><b>Didox kalitlari</b> o'rnatilgan</>
                    : <><b>Didox kalitlari yo'q</b> — token yoki login/parol berilmagan.</>} />
              )}

              <Chk ok={r.telegram.connected}
                text={r.telegram.connected
                  ? <><b>Telegram</b> ulangan — xabarlar boradi</>
                  : <><b>Telegram</b> ulanmagan. Botga <code>/start</code> yozing.</>} />

              <Chk ok={r.counterparties.noInn === 0}
                text={r.counterparties.noInn === 0
                  ? <><b>Kontragentlar</b> ({r.counterparties.total} ta) — hammasi INN bilan</>
                  : <><b>{r.counterparties.noInn} ta kontragentda INN yo'q</b>
                    {r.counterparties.sample.length > 0 &&
                      <>: {r.counterparties.sample.slice(0, 3).map(c => c.name).join(', ')}</>}
                    . Ularga kelgan hujjat noto'g'ri bog'lanishi mumkin.</>} />
            </>
          )}
        </div>
      </div>

      <div className="card" style={{ maxWidth: 560 }}>
        <div className="card-h">
          <h2>O'z tashkilotimiz</h2>
          <span className="hint">bir marta kiritiladi</span>
        </div>
        <div style={{ padding: '14px 16px', display: 'grid', gap: 12 }}>
          <Field label="Tashkilot nomi" required>
            <input className="inp" value={name} onChange={e => setName(e.target.value)} />
          </Field>
          <Field label="INN (9 raqam)" required hint="hujjat yo'nalishini shu bilan aniqlaymiz">
            <input className="inp mono" value={tin} onChange={e => setTin(e.target.value)} />
          </Field>
        </div>
        <div style={{ padding: '0 16px 14px' }}>
          <button className="btn pri" disabled={busy} onClick={save}>
            {busy ? 'Saqlanmoqda…' : 'Saqlash'}
          </button>
        </div>
      </div>
    </>
  )
}

function Chk({ ok, text }: { ok: boolean; text: React.ReactNode }) {
  return (
    <div style={{ display: 'flex', gap: 10, alignItems: 'flex-start', padding: '7px 0' }}>
      <span style={{
        color: ok ? 'var(--ok)' : 'var(--crit)',
        fontWeight: 700, flex: 'none', width: 16, textAlign: 'center',
      }}>{ok ? '✓' : '✗'}</span>
      <div style={{ fontSize: 13, color: 'var(--ink-2)', lineHeight: 1.55 }}>{text}</div>
    </div>
  )
}
