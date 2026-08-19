import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { api, setUnauthorizedHandler, storedUser, token, type Me, type Role } from './api'

interface AuthState {
  me: Me | null
  login: (username: string, password: string) => Promise<void>
  logout: () => void
  can: (...roles: Role[]) => boolean
}

const Ctx = createContext<AuthState>(null!)
export const useAuth = () => useContext(Ctx)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [me, setMe] = useState<Me | null>(storedUser.get())

  useEffect(() => {
    setUnauthorizedHandler(() => setMe(null))
    // Token bor bo'lsa serverdan tasdiqlaymiz — rol o'zgargan bo'lishi mumkin.
    if (token.get()) {
      api.get<Me>('/api/auth/me')
        .then(u => { setMe(u); storedUser.set(u) })
        .catch(() => setMe(null))
    }
  }, [])

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
    <Ctx.Provider value={{ me, login, logout, can }}>
      {children}
    </Ctx.Provider>
  )
}

export const roleName = (r: Role) =>
  r === 'Realizatsiya' ? 'Realizatsiya buxgalteri' : r === 'Perehod' ? 'Perehod buxgalteri' : 'Administrator'

export const initials = (fullName: string) =>
  fullName.split(/\s+/).slice(0, 2).map(w => w[0]?.toUpperCase() ?? '').join('')
