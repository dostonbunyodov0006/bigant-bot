// Dashboard Telegram ichida (Mini App) ochilganda ishlaydigan qism.
//
// Telegram sahifaga `window.Telegram.WebApp` obyektini beradi. Uning ichida
// `initData` — foydalanuvchi ma'lumoti va Telegram qo'ygan IMZO bor.
// Biz uni backendga yuboramiz, backend imzoni bot tokeni bilan tekshiradi.
// Shu sabab bugalter Telegram ichida login-parol kiritmaydi.
//
// Oddiy brauzerda bu obyekt yo'q — hammasi avvalgidek, login sahifasi chiqadi.

interface TgWebApp {
  initData: string
  colorScheme: 'light' | 'dark'
  ready: () => void
  expand: () => void
  themeParams?: Record<string, string>
  MainButton?: { hide: () => void }
}

declare global {
  interface Window {
    Telegram?: { WebApp?: TgWebApp }
  }
}

export const tg = (): TgWebApp | null => window.Telegram?.WebApp ?? null

/** Telegram ichidamizmi — imzolangan ma'lumot bo'lsa, ha. */
export const insideTelegram = () => {
  const w = tg()
  return !!w && typeof w.initData === 'string' && w.initData.length > 0
}

/**
 * Telegram oynasini tayyorlash: to'liq balandlikka yoyish va mavzuni
 * Telegram sozlamasiga moslash — ilova begona ko'rinmasin.
 */
export function prepareTelegram() {
  const w = tg()
  if (!w) return

  try {
    w.ready()
    w.expand()
    // Telegramda tungi rejim yoqilgan bo'lsa dashboard ham tungi bo'lsin.
    document.documentElement.dataset.theme = w.colorScheme === 'dark' ? 'dark' : 'light'
  } catch {
    // Telegramning eski versiyasi ba'zi metodlarni bilmasligi mumkin —
    // bu holatda ilova baribir ochilaveradi.
  }
}

export const initData = () => tg()?.initData ?? ''
