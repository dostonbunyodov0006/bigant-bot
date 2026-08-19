import { useEffect, useState } from 'react'

/**
 * O'QILMAGANLAR SONI — bitta manba.
 *
 * Eslatmalar sahifasi scroll paytida sonni bittalab kamaytiradi, yon
 * paneldagi qizil belgi esa o'sha zahoti shuni ko'rsatishi kerak. Server
 * so'rovini kutib tursak, belgi 15 soniyagacha eskirgan raqamda qolardi:
 * foydalanuvchi hammasini o'qib bo'lgan, belgi esa hali yonib turadi.
 *
 * Shuning uchun son shu yerda saqlanadi va kim tinglayotgan bo'lsa —
 * o'zgarishni darhol ko'radi.
 */

let current: number | null = null
let owner = false
const listeners = new Set<(n: number) => void>()

/** Sonni to'g'ridan-to'g'ri o'rnatish (egasi uchun). */
export function setUnread(n: number) {
  const v = Math.max(0, n)
  if (current === v) return
  current = v
  listeners.forEach(fn => fn(v))
}

/**
 * Serverdan kelgan son.
 *
 * Eslatmalar sahifasi ochiq bo'lsa — E'TIBORSIZ QOLDIRILADI. Sabab: scroll
 * paytida biz allaqachon 380 ga tushirgan bo'lamiz, server esa hali 400 ni
 * qaytaradi (o'qilganlar unga bir soniya kechroq yetadi). Uni qabul qilsak,
 * hisoblagich kamayib-ko'tarilib sakrardi.
 */
export function setUnreadFromServer(n: number) {
  if (!owner) setUnread(n)
}

/**
 * Sonni o'z qo'liga olish. Qaytgan funksiyani chaqirib bo'shatiladi —
 * shundan keyin yana server so'rovi boshqaradi.
 */
export function claimUnread() {
  owner = true
  return () => { owner = false }
}

/** Songa obuna bo'lish. */
export function useUnread(): number | null {
  const [n, setN] = useState<number | null>(current)

  useEffect(() => {
    listeners.add(setN)
    return () => { listeners.delete(setN) }
  }, [])

  return n
}
