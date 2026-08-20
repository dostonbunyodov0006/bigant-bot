import { useState } from 'react'
import { api } from '../lib/api'
import { errText } from '../lib/ui'

/**
 * «PAROLNI UNUTDIM» — TELEGRAM ORQALI TIKLASH.
 *
 * Ikki qadam: avval loginni aytadi, Telegramiga kod keladi; keyin o'sha
 * kod bilan yangi parol qo'yadi.
 *
 * Nega Telegram? Chunki rahbardan yuqorida hech kim yo'q — u parolini
 * unutsa, tiklab beradigan odam qolmaydi. Telegram esa har bir xodimga
 * allaqachon ulangan va faqat o'zida.
 */
export default function ParolniUnutdim({ onBack }: { onBack: () => void }) {
  const [step, setStep] = useState<'login' | 'kod'>('login')
  const [username, setUsername] = useState('')
  const [code, setCode] = useState('')
  const [next, setNext] = useState('')
  const [again, setAgain] = useState('')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)
  const [note, setNote] = useState<string | null>(null)
  const [done, setDone] = useState(false)

  const kodSora = async (e: React.FormEvent) => {
    e.preventDefault()
    setErr(null); setBusy(true)
    try {
      const r = await api.post<{ message: string }>('/api/auth/forgot', { username })
      setNote(r.message)
      setStep('kod')
    } catch (x) { setErr(errText(x)) } finally { setBusy(false) }
  }

  const tikla = async (e: React.FormEvent) => {
    e.preventDefault()
    setErr(null); setBusy(true)
    try {
      const r = await api.post<{ message: string }>('/api/auth/reset', { username, code, next })
      setNote(r.message); setDone(true)
    } catch (x) { setErr(errText(x)) } finally { setBusy(false) }
  }

  if (done) return (
    <div>
      <div className="alert ok" style={{ marginBottom: 14 }}><div>{note}</div></div>
      <button className="btn pri" style={{ width: '100%', justifyContent: 'center' }} onClick={onBack}>
        Kirish sahifasiga qaytish
      </button>
    </div>
  )

  return (
    <>
      {err && <div className="alert crit" style={{ marginBottom: 14 }}><div>{err}</div></div>}
      {note && step === 'kod' && !err &&
        <div className="alert info" style={{ marginBottom: 14 }}><div>{note}</div></div>}

      {step === 'login' ? (
        <form onSubmit={kodSora}>
          <div className="alert info" style={{ marginBottom: 14 }}>
            <div>Loginingizni yozing — Telegramingizga tiklash kodi yuboriladi.</div>
          </div>
          <label className="fld" style={{ marginBottom: 14 }}>
            <span className="lb">Login</span>
            <input className="inp" value={username} autoFocus autoComplete="username"
              onChange={e => setUsername(e.target.value)} required />
          </label>
          <button className="btn pri" style={{ width: '100%', justifyContent: 'center' }}
            disabled={busy || !username}>
            {busy ? 'Yuborilmoqda…' : 'Kod yuborish'}
          </button>
        </form>
      ) : (
        <form onSubmit={tikla}>
          <label className="fld" style={{ marginBottom: 12 }}>
            <span className="lb">Telegramga kelgan kod</span>
            <input className="inp mono" value={code} autoFocus inputMode="numeric" maxLength={6}
              onChange={e => setCode(e.target.value.replace(/\D/g, ''))} required />
          </label>
          <label className="fld" style={{ marginBottom: 12 }}>
            <span className="lb">Yangi parol</span>
            <input className="inp" type="password" value={next} autoComplete="new-password"
              onChange={e => setNext(e.target.value)} required />
            <span className="hint" style={{ fontSize: 11.5 }}>
              Kamida 8 belgi, ichida harf ham raqam ham bo'lsin
            </span>
          </label>
          <label className="fld" style={{ marginBottom: 14 }}>
            <span className="lb">Yangi parolni takrorlang</span>
            <input className="inp" type="password" value={again} autoComplete="new-password"
              onChange={e => setAgain(e.target.value)} required />
            {again.length > 0 && next !== again &&
              <span className="hint" style={{ fontSize: 11.5, color: 'var(--crit)' }}>
                Ikki parol bir xil emas
              </span>}
          </label>
          <button className="btn pri" style={{ width: '100%', justifyContent: 'center' }}
            disabled={busy || code.length !== 6 || next.length < 8 || next !== again}>
            {busy ? 'Saqlanmoqda…' : 'Yangi parolni saqlash'}
          </button>
        </form>
      )}

      <button className="btn" style={{ width: '100%', justifyContent: 'center', marginTop: 10 }}
        onClick={onBack} type="button">
        Orqaga
      </button>
    </>
  )
}
