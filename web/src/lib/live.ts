import { useEffect, useRef, useState } from 'react'
import { token } from './api'

export interface LiveNotification {
  id: number
  kind: string
  severity: 'Info' | 'Warning' | 'Critical'
  title: string
  body: string
  dealId: number | null
  createdAt: string
}

/**
 * JONLI OQIM — serverdan kelayotgan xabarlar.
 *
 * So'rov yuborib turish (polling) o'rniga server o'zi bildiradi: bugalter
 * botga «5 mln o'tkazdik» deb yozsa, dashboardda bir soniya ichida ko'rinadi.
 *
 * `onMessage` — yangi xabar kelganda chaqiriladi. Odatda unda ekranni
 * qayta yuklash kifoya.
 *
 * EventSource sarlavha qo'sha olmaydi, shuning uchun token manzilda ketadi —
 * ulanish bir xil manbada (dashboard backenddan tarqatiladi), demak token
 * tashqariga chiqmaydi.
 */
export function useLive(onMessage: (items: LiveNotification[]) => void) {
  const [connected, setConnected] = useState(false)
  const cbRef = useRef(onMessage)
  cbRef.current = onMessage

  useEffect(() => {
    const t = token.get()
    if (!t) return

    let es: EventSource | null = null
    let retry: number | undefined
    let closed = false

    const open = () => {
      if (closed) return
      es = new EventSource(`/api/live?access_token=${encodeURIComponent(t)}`)

      es.addEventListener('ulandi', () => setConnected(true))

      es.addEventListener('xabar', e => {
        try {
          cbRef.current(JSON.parse((e as MessageEvent).data))
        } catch { /* buzuq yozuvni jimgina o'tkazamiz */ }
      })

      es.onerror = () => {
        setConnected(false)
        es?.close()
        // Server qayta ishga tushgan bo'lishi mumkin — qayta ulanamiz.
        retry = window.setTimeout(open, 3000)
      }
    }

    open()
    return () => {
      closed = true
      if (retry) clearTimeout(retry)
      es?.close()
    }
  }, [])

  return connected
}
