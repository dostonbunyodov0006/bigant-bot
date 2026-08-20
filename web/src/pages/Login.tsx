import { useState } from 'react'
import { useAuth } from '../lib/auth'
import { errText } from '../lib/ui'
import ParolniUnutdim from '../components/ParolniUnutdim'

export default function Login() {
  const { login } = useAuth()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)

  // Parolni unutgan odam shu yerdan Telegram orqali tiklaydi. Alohida
  // sahifa emas, o'sha oynaning ichi almashadi — bir qadam kam.
  const [unutdim, setUnutdim] = useState(false)

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    setBusy(true)
    setErr(null)
    try {
      await login(username, password)
    } catch (x) {
      setErr(errText(x))
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="login">
      <div className="box">
        {/* Kirish sahifasida joy bor — firma rasmi to'liq ko'rsatiladi.
            Yon panelda esa u kesib olinadi (40 piksel juda kichik). */}
        <div style={{ marginBottom: 18, textAlign: 'center' }}>
          <img className="login-logo" src="/bigant-logo.jpg" alt="Bigant" />
          <div className="sub" style={{ margin: '10px 0 0' }}>Hujjat va qarz nazorati</div>
        </div>

        {unutdim ? (
          <ParolniUnutdim onBack={() => { setUnutdim(false); setErr(null) }} />
        ) : (
          <form onSubmit={submit}>
            {err && <div className="alert crit" style={{ marginBottom: 14 }}><div>{err}</div></div>}

            <div className="f">
              <label className="fld">
                <span className="lb">Login</span>
                <input className="inp" value={username} onChange={e => setUsername(e.target.value)}
                  autoFocus autoComplete="username" required />
              </label>
            </div>
            <div className="f">
              <label className="fld">
                <span className="lb">Parol</span>
                <input className="inp" type="password" value={password}
                  onChange={e => setPassword(e.target.value)} autoComplete="current-password" required />
              </label>
            </div>

            <button className="btn pri" style={{ width: '100%', justifyContent: 'center', marginTop: 6 }}
              disabled={busy || !username || !password}>
              {busy ? 'Kirilmoqda…' : 'Kirish'}
            </button>

            {/* Parolni unutgan odam dashboard ICHIGA kira olmaydi —
                shuning uchun tiklash faqat shu yerda, kirish ekranida
                bo'lishi kerak. Uchala rol ham shu tugmadan foydalanadi. */}
            <button type="button" className="btn forgot-btn"
              onClick={() => { setUnutdim(true); setErr(null) }}>
              Parolni unutdim
            </button>
          </form>
        )}
      </div>
    </div>
  )
}
