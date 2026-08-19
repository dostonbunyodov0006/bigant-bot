import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { api, setUnauthorizedHandler, storedUser, token, type Me, type Role } from './api'
import { initData, insideTelegram, prepareTelegram } from './telegram'

interface AuthState {
  me: Me | null
  /** Telegram ichida ochilganmi — bunda "Chiqish" tugmasi ma'nosiz. */
  inTelegram: boolean
  /** Telegram bilan kirish hali davom etyaptimi. */
  checking: boolean
  login: (username: string, password: string) => Promise<void>
  logout: () => void
  can: (...roles: Role[]) => boolean
}

const Ctx = createContext<AuthState>(null!)
export const useAuth = () => useContext(Ctx)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [me, setMe] = useState<Me | null>(storedUser.get())
  const inTelegram = insideTelegram()
  // Telegram ichida bo'lsak kirish jarayoni tugaguncha login sahifasini
  // ko'rsatmaymiz — aks holda ekran bir lahzaga "parol kiriting" deb chaqnaydi.
  const [checking, setChecking] = useState(inTelegram)

  useEffect(() => {
    setUnauthorizedHandler(() => setMe(null))

    if (inTelegram) {
      prepareTelegram()
      // TELEGRAM ORQALI KIRISH — parol so'ralmaydi.
      api.post<{ token: string; user: Me }>('/api/auth/telegram', { initData: initData() })
        .then(r => { token.set(r.token); storedUser.set(r.user); setMe(r.user) })
        .catch(() => { token.clear(); setMe(null) })
        .finally(() => setChecking(false))
      return
    }

    // Token bor bo'lsa serverdan tasdiqlaymiz — rol o'zgargan bo'lishi mumkin.
    if (token.get()) {
      api.get<Me>('/api/auth/me')
        .then(u => { setMe(u); storedUser.set(u) })
        .catch(() => setMe(null))
    }
  }, [inTelegram])

  const login = async (username: string, password: string) => {
    const r = await api.post<{ token: string; user: Me }>('/api/auth/login', { username, password })
    token.set(r.token)
    storedUser.set(r.user)
    setMe(r.user)
  }

  const logout = () => {
    token.clear()
    setMe(null)
  }

  const can = (...roles: Role[]) => !!me && roles.includes(me.role)

  return (
    <Ctx.Provider value={{ me, inTelegram, checking, login, logout, can }}>
      {children}
    </Ctx.Provider>
  )
}

export const roleName = (r: Role) =>
  r === 'Realizatsiya' ? 'Realizatsiya buxgalteri' : r === 'Perehod' ? 'Perehod buxgalteri' : 'Admin'

export const initials = (fullName: string) =>
  fullName.split(/\s+/).slice(0, 2).map(w => w[0]?.toUpperCase() ?? '').join('')
