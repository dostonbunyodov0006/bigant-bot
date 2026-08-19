import { useState } from 'react'
import { useAuth } from '../lib/auth'
import { errText } from '../lib/ui'

export default function Login() {
  const { login } = useAuth()
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [err, setErr] = useState<string | null>(null)

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
      <form className="box" onSubmit={submit}>
        <div style={{ display: 'flex', gap: 10, alignItems: 'center', marginBottom: 18 }}>
          <div className="brand-mark">B</div>
          <div>
            <h1>Bigant</h1>
            <div className="sub" style={{ margin: 0 }}>Hujjat oqimi tizimi</div>
          </div>
        </div>

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
      </form>
    </div>
  )
}
