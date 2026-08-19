import { useEffect } from 'react'
import { NavLink, Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { useAuth, initials, roleName } from './lib/auth'
import type { Role } from './lib/api'
import Login from './pages/Login'
import Realizatsiya from './pages/Realizatsiya'
import Perehod from './pages/Perehod'
import AdminView from './pages/AdminView'
import Deals from './pages/Deals'
import DealCardPage from './pages/DealCard'
import Inbox from './pages/Inbox'
import Notifications from './pages/Notifications'
import Setup from './pages/Setup'

interface NavItem {
  to: string
  label: string
  hint: string
  ic: string
  roles: Role[]
  group: string
}

/**
 * NAVIGATSIYA — har rol faqat o'z ishini ko'radi.
 * `hint` — bo'lim nima uchun ekanini bir gapda aytadi, chunki bu tizimga
 * kunda bir marta kiriladigan odamlar ham bor.
 */
const NAV: NavItem[] = [
  { to: '/realizatsiya', label: 'Realizatsiya', hint: 'nima sotdik, puli keldimi',
    ic: '◈', roles: ['Realizatsiya', 'Admin'], group: 'Mening ishim' },
  { to: '/perehod', label: 'Perehod', hint: 'hujjat zanjiri butunmi',
    ic: '⇄', roles: ['Perehod', 'Admin'], group: 'Mening ishim' },
  { to: '/admin', label: 'Umumiy manzara', hint: 'hamma bitim, ikkala yo\'nalish',
    ic: '▣', roles: ['Admin'], group: 'Mening ishim' },

  { to: '/deals', label: 'Shartnomalar', hint: 'to\'liq ro\'yxat va kartochka',
    ic: '≡', roles: ['Realizatsiya', 'Perehod', 'Admin'], group: 'Hujjatlar' },
  { to: '/inbox', label: 'Kiruvchi', hint: 'Didoxdan kelgan hujjatlar',
    ic: '↓', roles: ['Perehod', 'Admin'], group: 'Hujjatlar' },

  { to: '/notifications', label: 'Eslatmalar', hint: 'ko\'rmay qolmaslik uchun',
    ic: '◔', roles: ['Realizatsiya', 'Perehod', 'Admin'], group: 'Boshqa' },
  { to: '/setup', label: 'Sozlamalar', hint: 'rekvizitlar, xodimlar, Didox',
    ic: '⚙', roles: ['Admin'], group: 'Boshqa' },
]

const TITLES: Record<string, [string, string]> = {
  '/realizatsiya': ['Realizatsiya buxgalteriyasi', 'sotuv hujjatlari va hisob-kitob'],
  '/perehod': ['Perehod buxgalteriyasi', 'hujjatlar harakati va zanjir'],
  '/admin': ['Umumiy manzara', 'hamma bitim bir joyda'],
  '/deals': ['Shartnomalar', 'sotuv va xarid'],
  '/inbox': ['Kiruvchi hujjatlar', 'postavshikdan va klientdan'],
  '/notifications': ['Eslatmalar', 'e\'tibor talab qiladigan holatlar'],
  '/setup': ['Sozlamalar', 'rekvizitlar · xodimlar · Didox tayyorligi'],
}

/** Rol kirganda birinchi ko'radigan sahifa — o'z ishi. */
const HOME: Record<Role, string> = {
  Realizatsiya: '/realizatsiya',
  Perehod: '/perehod',
  Admin: '/admin',
}

function Shell() {
  const { me, logout } = useAuth()
  const loc = useLocation()

  // Rol tusini butun sahifaga beramiz — foydalanuvchi qaysi ekranda
  // turganini rangdan ham sezadi.
  useEffect(() => {
    if (me) document.documentElement.setAttribute('data-role', me.role)
    return () => document.documentElement.removeAttribute('data-role')
  }, [me])

  if (!me) return <Navigate to="/login" replace />

  const items = NAV.filter(n => n.roles.includes(me.role))
  const groups = [...new Set(items.map(i => i.group))]

  const key = loc.pathname.startsWith('/deals/') ? '/deals' : loc.pathname
  const t = TITLES[key] ?? ['Shartnoma kartochkasi', '']

  return (
    <div className="app">
      <aside className="side">
        <div className="brand">
          <div className="brand-mark">B</div>
          <div>
            <div className="brand-name">Bigant</div>
            <div className="role-tag">{roleName(me.role)}</div>
          </div>
        </div>

        <nav className="nav">
          {groups.map(g => (
            <div key={g}>
              <div className="nav-label">{g}</div>
              {items.filter(i => i.group === g).map(i => (
                <NavLink key={i.to} to={i.to} title={i.hint}
                  className={({ isActive }) => (isActive ? 'on' : '')}>
                  <span className="ic">{i.ic}</span>
                  {i.label}
                </NavLink>
              ))}
            </div>
          ))}
        </nav>

        <div className="side-foot">
          <div>Xabarlar Telegram botga ham boradi</div>
        </div>
      </aside>

      <div className="main">
        <header className="top">
          <div>
            <h1>{t[0]}</h1>
            <div className="top-sub">{t[1]}</div>
          </div>
          <div className="top-right">
            <div style={{ textAlign: 'right', fontSize: 12.5, lineHeight: 1.3 }}>
              <div style={{ fontWeight: 600 }}>{me.fullName}</div>
              <div style={{ color: 'var(--ink-3)' }}>{roleName(me.role)}</div>
            </div>
            <div className="avatar" title={me.fullName}>{initials(me.fullName)}</div>
            <button className="btn sm" onClick={logout}>Chiqish</button>
          </div>
        </header>

        <div className="wrap">
          <Routes>
            <Route path="/" element={<Navigate to={HOME[me.role]} replace />} />
            <Route path="/realizatsiya" element={<Guard ok={me.role !== 'Perehod'}><Realizatsiya /></Guard>} />
            <Route path="/perehod" element={<Guard ok={me.role !== 'Realizatsiya'}><Perehod /></Guard>} />
            <Route path="/admin" element={<Guard ok={me.role === 'Admin'}><AdminView /></Guard>} />
            <Route path="/deals" element={<Deals />} />
            <Route path="/deals/:id" element={<DealCardPage />} />
            <Route path="/inbox" element={<Guard ok={me.role !== 'Realizatsiya'}><Inbox /></Guard>} />
            <Route path="/notifications" element={<Notifications />} />
            <Route path="/setup" element={<Guard ok={me.role === 'Admin'}><Setup /></Guard>} />
            <Route path="*" element={<div className="empty">Sahifa topilmadi</div>} />
          </Routes>
        </div>
      </div>
    </div>
  )
}

/** Rolga tegishli bo'lmagan sahifa — bo'sh ekran o'rniga tushunarli javob. */
function Guard({ ok, children }: { ok: boolean; children: React.ReactNode }) {
  if (ok) return <>{children}</>
  return (
    <div className="alert warn">
      <div>
        <b>Bu bo'lim sizning ishingizga kirmaydi.</b><br />
        Har rol faqat o'z jarayonini ko'radi — shunda chalkashlik bo'lmaydi.
        Kerak bo'lsa administratordan so'rang.
      </div>
    </div>
  )
}

export default function App() {
  const { me } = useAuth()
  return (
    <Routes>
      <Route path="/login" element={me ? <Navigate to="/" replace /> : <Login />} />
      <Route path="/*" element={<Shell />} />
    </Routes>
  )
}
