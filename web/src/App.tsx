import { useEffect } from 'react'
import { NavLink, Navigate, Route, Routes } from 'react-router-dom'
import { useAuth, initials, roleName } from './lib/auth'
import { setUnreadFromServer, useUnread } from './lib/unread'
import { useAsync } from './lib/ui'
import { api, type Role } from './lib/api'
import { Logo } from './components/Logo'
import Login from './pages/Login'
import { ErrorGuard } from './components/ErrorGuard'
import Realizatsiya from './pages/Realizatsiya'
import Perehod from './pages/Perehod'
import AdminView from './pages/AdminView'
import Hisobot from './pages/Hisobot'
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
  { to: '/realizatsiya', label: 'Realizatsiya buxgalteriya', hint: 'nima sotdik, puli keldimi',
    ic: '◈', roles: ['Realizatsiya', 'Admin'], group: 'Kundalik ish' },
  { to: '/perehod', label: 'Perehod buxgalteriya', hint: 'hujjatlar to\'liq yig\'ildimi',
    ic: '⇄', roles: ['Perehod', 'Admin'], group: 'Kundalik ish' },
  { to: '/admin', label: 'Barcha shartnomalar', hint: 'sotuv va xarid — birgalikda',
    ic: '▣', roles: ['Admin'], group: 'Kundalik ish' },

  { to: '/hisobot', label: 'Oylik hisobot', hint: 'bir oylik to\'liq manzara',
    ic: '▤', roles: ['Realizatsiya', 'Perehod', 'Admin'], group: 'Kundalik ish' },

  { to: '/deals', label: 'Shartnomalar', hint: 'har bir shartnoma: summa, to\'lov, qarz',
    ic: '≡', roles: ['Realizatsiya', 'Perehod', 'Admin'], group: 'Hujjatlar' },
  { to: '/inbox', label: 'Kelgan hujjatlar', hint: 'Didoxdan tushgan hujjatlar oqimi',
    ic: '↓', roles: ['Perehod', 'Admin'], group: 'Hujjatlar' },

  { to: '/notifications', label: 'Eslatmalar', hint: 'botga kelgan xabarlar shu yerda ham',
    ic: '◔', roles: ['Realizatsiya', 'Perehod', 'Admin'], group: 'Sozlash' },
  { to: '/setup', label: 'Sozlamalar', hint: 'tizim holati va Bigant INN',
    ic: '⚙', roles: ['Admin'], group: 'Sozlash' },
]

/** Rol kirganda birinchi ko'radigan sahifa — o'z ishi. */
const HOME: Record<Role, string> = {
  Realizatsiya: '/realizatsiya',
  Perehod: '/perehod',
  Admin: '/admin',
}

function Shell() {
  const { me, logout, inTelegram, checking } = useAuth()

  // O'qilmagan eslatma soni. Serverdan vaqti-vaqti bilan yangilanadi, lekin
  // Eslatmalar sahifasi scroll paytida uni DARHOL kamaytiradi — belgi
  // eskirgan raqamda turib qolmasin.
  useAsync<{ unread: number }>(
    () => api.get<{ unread: number }>('/api/notifications?take=1')
            .then(r => { setUnreadFromServer(r.unread); return r }), [], 15000)
  const unread = useUnread() ?? 0

  // Rol tusini butun sahifaga beramiz — foydalanuvchi qaysi ekranda
  // turganini rangdan ham sezadi.
  useEffect(() => {
    if (me) document.documentElement.setAttribute('data-role', me.role)
    return () => document.documentElement.removeAttribute('data-role')
  }, [me])

  // Telegram bizni tanib olguncha kutamiz — login sahifasi chaqnab ketmasin.
  if (checking) return <div className="boot">Telegram orqali kirilmoqda…</div>
  if (!me) return <Navigate to="/login" replace />

  const items = NAV.filter(n => n.roles.includes(me.role))
  const groups = [...new Set(items.map(i => i.group))]


  return (
    <div className="app">
      <aside className="side">
        <div className="brand">
          <Logo sub={roleName(me.role)} />
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
                  {i.to === '/notifications' && unread > 0 &&
                    <span className="badge">{unread}</span>}
                </NavLink>
              ))}
            </div>
          ))}
        </nav>

        <div className="side-foot user-foot">
          <div className="avatar">{initials(me.fullName)}</div>
          <div className="user-info">
            <div className="user-name">{me.fullName}</div>
            <div className="user-role">{roleName(me.role)}</div>
          </div>
          {/* Telegram ichida chiqishning ma'nosi yo'q: keyingi ochilishda
              Telegram baribir o'sha odamni tanidi. */}
          {!inTelegram &&
            <button className="btn sm" onClick={logout} title="Tizimdan chiqish">Chiqish</button>}
        </div>
      </aside>

      <div className="main">
        <div className="wrap">
          {/* Sahifa chizilmasa oq ekran emas, o'qiladigan xato ko'rinsin. */}
          <ErrorGuard>
          <Routes>
            <Route path="/" element={<Navigate to={HOME[me.role]} replace />} />
            <Route path="/realizatsiya" element={<Guard ok={me.role !== 'Perehod'}><Realizatsiya /></Guard>} />
            <Route path="/perehod" element={<Guard ok={me.role !== 'Realizatsiya'}><Perehod /></Guard>} />
            <Route path="/admin" element={<Guard ok={me.role === 'Admin'}><AdminView /></Guard>} />
            <Route path="/hisobot" element={<Hisobot />} />
            <Route path="/deals" element={<Deals />} />
            <Route path="/deals/:id" element={<DealCardPage />} />
            <Route path="/inbox" element={<Guard ok={me.role !== 'Realizatsiya'}><Inbox /></Guard>} />
            <Route path="/notifications" element={<Notifications />} />
            <Route path="/setup" element={<Guard ok={me.role === 'Admin'}><Setup /></Guard>} />
            <Route path="*" element={<div className="empty">Sahifa topilmadi</div>} />
          </Routes>
          </ErrorGuard>
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
  const { me, inTelegram, checking } = useAuth()

  if (inTelegram && checking) return <div className="boot">Telegram orqali kirilmoqda…</div>

  return (
    <Routes>
      <Route path="/login" element={
        me ? <Navigate to="/" replace />
           : inTelegram ? <TelegramDenied /> : <Login />} />
      <Route path="/*" element={<Shell />} />
    </Routes>
  )
}

/**
 * Telegram odamni tanidi, lekin u tizimda yo'q. Bu parol xatosi emas —
 * shu sabab login formasini ko'rsatish chalg'itadi.
 */
function TelegramDenied() {
  return (
    <div className="boot boot-deny">
      <strong>Ruxsat yo'q</strong>
      <p>Telegram akkauntingiz tizimga biriktirilmagan.<br />Administratorga murojaat qiling.</p>
    </div>
  )
}
