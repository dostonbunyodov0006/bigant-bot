import {
  createContext, useCallback, useContext, useEffect, useRef, useState,
  type ReactNode,
} from 'react'
import { ApiError } from './api'

// ══════════ Toast ══════════
type Toast = { id: number; text: string; err?: boolean }
const ToastCtx = createContext<(text: string, err?: boolean) => void>(() => {})
export const useToast = () => useContext(ToastCtx)

export function ToastHost({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<Toast[]>([])
  const seq = useRef(0)

  const push = useCallback((text: string, err = false) => {
    const id = ++seq.current
    setItems(v => [...v, { id, text, err }])
    setTimeout(() => setItems(v => v.filter(x => x.id !== id)), err ? 5000 : 3000)
  }, [])

  return (
    <ToastCtx.Provider value={push}>
      {children}
      {items.length > 0 && (
        <div style={{ position: 'fixed', bottom: 20, left: '50%', transform: 'translateX(-50%)', zIndex: 60, display: 'flex', flexDirection: 'column', gap: 8 }}>
          {items.map(t => (
            <div key={t.id} className={'toast' + (t.err ? ' err' : '')} role="status" style={{ position: 'static', transform: 'none' }}>
              {t.text}
            </div>
          ))}
        </div>
      )}
    </ToastCtx.Provider>
  )
}

/** Xatoni foydalanuvchiga tushunarli matnga aylantiradi. */
export const errText = (e: unknown) =>
  e instanceof ApiError ? e.message : e instanceof Error ? e.message : 'Kutilmagan xato'

// ══════════ Ma'lumot yuklash ══════════
export function useAsync<T>(fn: () => Promise<T>, deps: unknown[] = []) {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [tick, setTick] = useState(0)
  const fnRef = useRef(fn)
  fnRef.current = fn

  useEffect(() => {
    let alive = true
    setLoading(true)
    setError(null)
    fnRef.current()
      .then(d => { if (alive) setData(d) })
      .catch(e => { if (alive) setError(errText(e)) })
      .finally(() => { if (alive) setLoading(false) })
    return () => { alive = false }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick])

  return { data, error, loading, reload: () => setTick(t => t + 1) }
}

// ══════════ Kichik komponentlar ══════════
export const Pill = ({ kind, children }: { kind: 'ok' | 'warn' | 'crit' | 'neut' | 'acc'; children: ReactNode }) =>
  <span className={`pill ${kind}`}>{children}</span>

export function Seg<T extends string>({ value, onChange, options }: {
  value: T
  onChange: (v: T) => void
  options: { v: T; label: string }[]
}) {
  return (
    <div className="seg">
      {options.map(o => (
        <button key={o.v} type="button" aria-pressed={value === o.v} onClick={() => onChange(o.v)}>
          {o.label}
        </button>
      ))}
    </div>
  )
}

export function Field({ label, required, hint, full, children }: {
  label: string; required?: boolean; hint?: string; full?: boolean; children: ReactNode
}) {
  return (
    <label className={'fld' + (full ? ' full' : '')}>
      <span className="lb">{label}{required && <span className="req"> *</span>}</span>
      {children}
      {hint && <span style={{ fontSize: 11, color: 'var(--ink-3)', display: 'block', marginTop: 3 }}>{hint}</span>}
    </label>
  )
}

export function Modal({ title, onClose, onOk, okLabel = 'Saqlash', busy, children }: {
  title: string
  onClose: () => void
  onOk?: () => void
  okLabel?: string
  busy?: boolean
  children: ReactNode
}) {
  useEffect(() => {
    const h = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    window.addEventListener('keydown', h)
    return () => window.removeEventListener('keydown', h)
  }, [onClose])

  return (
    <div className="modal-bg" onClick={e => { if (e.target === e.currentTarget) onClose() }}>
      <div className="modal" role="dialog" aria-modal="true" aria-label={title}>
        <div className="modal-h">
          <h2>{title}</h2>
          <button className="btn sm" onClick={onClose} aria-label="Yopish">✕</button>
        </div>
        <div className="modal-b">{children}</div>
        {onOk && (
          <div className="modal-f">
            <button className="btn" onClick={onClose}>Bekor</button>
            <button className="btn pri" onClick={onOk} disabled={busy}>
              {busy ? 'Kutilmoqda…' : okLabel}
            </button>
          </div>
        )}
      </div>
    </div>
  )
}

export const Loading = () => <div className="empty">Yuklanmoqda…</div>
export const ErrorBox = ({ text }: { text: string }) =>
  <div className="alert crit" style={{ margin: 16 }}><div>{text}</div></div>
