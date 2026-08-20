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
/**
 * Ma'lumot yuklash.
 *
 * `refreshMs` berilsa — sahifa o'zi yangilanib turadi. Bu shuning uchun kerak:
 * bugalter Telegram botga «5 mln o'tkazdik» deb yozganda, dashboardda ham
 * o'sha zahoti ko'rinishi kerak. Sahifani qo'lda yangilash — noqulaylik.
 *
 * Avtomatik yangilanishda «Yuklanmoqda» chiqmaydi (ekran o'chib-yonmasin),
 * faqat birinchi yuklashda chiqadi.
 */
export function useAsync<T>(fn: () => Promise<T>, deps: unknown[] = [], refreshMs = 0) {
  const [data, setData] = useState<T | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [tick, setTick] = useState(0)
  const fnRef = useRef(fn)
  fnRef.current = fn
  const firstRef = useRef(true)

  useEffect(() => {
    let alive = true
    if (firstRef.current) setLoading(true)
    setError(null)
    fnRef.current()
      .then(d => { if (alive) setData(d) })
      .catch(e => { if (alive) setError(errText(e)) })
      .finally(() => { if (alive) { setLoading(false); firstRef.current = false } })
    return () => { alive = false }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [...deps, tick])

  // Fon yangilanishi. Ko'z bilan kuzatib turiladigan ekran uchun.
  useEffect(() => {
    if (refreshMs <= 0) return
    const id = setInterval(() => {
      // Ilova fonda bo'lsa so'rov yubormaymiz — bekorga yuklama bo'lmasin.
      if (document.visibilityState === 'visible') setTick(t => t + 1)
    }, refreshMs)
    return () => clearInterval(id)
  }, [refreshMs])

  return { data, error, loading, reload: () => setTick(t => t + 1) }
}

// ══════════ Kichik komponentlar ══════════
export const Pill = ({ kind, children }: { kind: 'ok' | 'warn' | 'crit' | 'neut' | 'acc'; children: ReactNode }) =>
  <span className={`pill ${kind}`}>{children}</span>

export const Loading = () => <div className="empty">Yuklanmoqda…</div>
export const ErrorBox = ({ text }: { text: string }) =>
  <div className="alert crit" style={{ margin: 16 }}><div>{text}</div></div>
