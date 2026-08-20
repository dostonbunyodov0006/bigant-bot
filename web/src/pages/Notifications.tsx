import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, dateTimeUz, type Notif } from '../lib/api'
import { ErrorBox, Loading, errText, useToast } from '../lib/ui'
import { claimUnread, setUnread } from '../lib/unread'

/** O'qilganlarni serverga shuncha kutib, to'plab yuboramiz — har biriga alohida so'rov ketmasin. */
const FLUSH_MS = 900

/**
 * Bir martada nechta yuklanadi.
 *
 * Chegara EMAS — pastga tushgan sayin keyingisi o'zi kelaveradi, eslatma
 * qolmagunicha. Hammasini bitta so'rovda tortib olsak, minglab qator
 * brauzerni qotirib qo'yadi; bo'lib olsak, foydalanuvchi buni sezmaydi.
 */
const PAGE = 100

/** Rol nomi — xabar qaysi ishga tegishli ekanini bir so'zda aytadi. */
const ROLE_UZ: Record<string, string> = {
  Realizatsiya: 'realizatsiya',
  Perehod: 'perehod',
  Admin: 'tizim',
}

export default function Notifications() {
  const nav = useNavigate()
  const toast = useToast()

  // Xabarlar rolga qarab bo'linadi. Admin hammasini ko'radi, shuning uchun
  // unga «kimga tegishli» degan filtr kerak — aks holda uch xil ishning
  // xabari bitta ro'yxatda aralashib yotadi.
  const [role, setRole] = useState<'all' | 'Realizatsiya' | 'Perehod' | 'Admin'>('all')

  const [items, setItems] = useState<Notif[]>([])
  const [unreadAtLoad, setUnreadAtLoad] = useState(0)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [end, setEnd] = useState(false)      // boshqa eslatma qolmadi
  const [busy, setBusy] = useState(false)    // keyingi sahifa kelmoqda

  const fetching = useRef(false)

  const loadPage = useCallback(async (skip: number) => {
    if (fetching.current) return
    fetching.current = true
    if (skip > 0) setBusy(true)
    try {
      const r = await api.get<{ unread: number; items: Notif[] }>(
        `/api/notifications?take=${PAGE}&skip=${skip}`)

      // O'qilmaganlar sonini FAQAT birinchi yuklashda olamiz. Keyingi
      // sahifalarda server allaqachon kamaygan sonni qaytaradi va biz uni
      // yana `seen` ga ayirsak, hisoblagich ikki marta kamayib ketardi.
      if (skip === 0) { setUnreadAtLoad(r.unread); setUnread(r.unread) }

      setItems(prev => (skip === 0 ? r.items : [...prev, ...r.items]))
      if (r.items.length < PAGE) setEnd(true)
      setError(null)
    } catch (e) {
      setError(errText(e))
    } finally {
      fetching.current = false
      setBusy(false)
      setLoading(false)
    }
  }, [])

  useEffect(() => { loadPage(0) }, [loadPage])

  // ── KO'RGAN = O'QIGAN ────────────────────────────────────────────────
  // Bugalter eslatmalarni ko'zdan kechiradi; pastga tushib bo'lgach ro'yxat
  // o'z-o'zidan tozalanishi kerak. Tugma bosish shart emas.
  const [seen, setSeen] = useState<ReadonlySet<number>>(() => new Set())

  const queue = useRef<Set<number>>(new Set())
  const flushTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  const flush = useCallback(() => {
    const ids = [...queue.current]
    queue.current.clear()
    if (ids.length === 0) return
    // Xato bo'lsa jim o'tamiz: eslatma o'qilmagan bo'lib qoladi, xolos.
    api.post('/api/notifications/read', { ids }).catch(() => {})
  }, [])

  const markSeen = useCallback((id: number) => {
    if (queue.current.has(id)) return
    queue.current.add(id)
    setSeen(prev => (prev.has(id) ? prev : new Set(prev).add(id)))

    if (flushTimer.current) clearTimeout(flushTimer.current)
    flushTimer.current = setTimeout(flush, FLUSH_MS)
  }, [flush])

  // Sahifadan chiqilganda to'planganini yuborib qo'yamiz — aks holda
  // oxirgi bir nechtasi o'qilmagan bo'lib qolardi.
  useEffect(() => () => {
    if (flushTimer.current) clearTimeout(flushTimer.current)
    flush()
  }, [flush])

  // ── KUZATUVCHI ───────────────────────────────────────────────────────
  const observer = useRef<IntersectionObserver | null>(null)

  // Qatorlar kuzatuvchidan OLDIN tug'iladi: React avval ref larni ulaydi,
  // keyin effektlarni ishga tushiradi. Shu sabab elementlarni saqlab
  // turamiz va kuzatuvchi paydo bo'lgach hammasini qo'shamiz.
  const els = useRef(new Map<number, Element>())

  useEffect(() => {
    const io = new IntersectionObserver(entries => {
      for (const e of entries) {
        const id = Number((e.target as HTMLElement).dataset.id)
        if (!id) continue

        // Uchdan biri ko'ringan bo'lsa — ko'rilgan hisoblanadi.
        // Ikkinchi shart uzun eslatmalar uchun: matni ekrandan ham baland
        // bo'lsa, u hech qachon 30% ga yetmaydi va hisoblagich shu yerda
        // qotib qolardi.
        const visible = e.isIntersecting &&
          (e.intersectionRatio >= 0.3 ||
           e.intersectionRect.height >= window.innerHeight * 0.5)

        // Kutish yo'q: ko'rindi — o'sha zahoti o'qilgan.
        if (!visible) continue
        markSeen(id)
        io.unobserve(e.target)
      }
    }, { threshold: [0, 0.3] })

    observer.current = io
    els.current.forEach(el => io.observe(el))

    return () => {
      io.disconnect()
      observer.current = null
    }
  }, [markSeen])

  const watch = useCallback((el: HTMLDivElement | null) => {
    if (!el) return
    const id = Number(el.dataset.id)
    if (!id) return
    els.current.set(id, el)
    observer.current?.observe(el)
  }, [])

  // ── PASTGA YETGANDA KEYINGISINI YUKLASH ──────────────────────────────
  // Ro'yxat oxiridagi ko'rinmas belgi ekranga kirsa — keyingi sahifa keladi.
  // Foydalanuvchi uchun bu «cheksiz ro'yxat» bo'lib tuyuladi.
  const tailRef = useCallback((el: HTMLDivElement | null) => {
    if (!el) return
    const io = new IntersectionObserver(entries => {
      if (entries.some(e => e.isIntersecting) && !end && !fetching.current) {
        loadPage(items.length)
      }
    }, { rootMargin: '400px' })   // yetib borguncha oldindan yuklaymiz
    io.observe(el)
    return () => io.disconnect()
  }, [end, items.length, loadPage])

  // Scroll paytida son bittalab kamayadi. Yon paneldagi qizil belgi ham
  // shu zahoti kamayishi uchun umumiy manbaga yozib boramiz.
  // Rol bo'yicha saralangan ro'yxat. Roli ko'rsatilmagan xabar ikkala
  // buxgalterga ham tegishli — shuning uchun ular filtrga tushmaydi.
  const shown = role === 'all'
    ? items
    : items.filter(n => n.targetRole === role || n.targetRole == null)

  const unread = Math.max(0, unreadAtLoad - seen.size)
  useEffect(() => { setUnread(unread) }, [unread])

  // Sahifa ochiq turganda son shu yerdan boshqariladi — 15 soniyalik
  // server so'rovi uni orqaga ko'tarib yubormasin.
  useEffect(() => claimUnread(), [])

  if (loading) return <Loading />
  if (error && items.length === 0) return <ErrorBox text={error} />

  const markAll = async () => {
    try {
      const r = await api.post<{ marked: number }>('/api/notifications/read-all')
      toast(`${r.marked} ta eslatma o'qilgan deb belgilandi`)
      // Hammasini boshidan yuklaymiz — holat serverniki bilan bir xil bo'lsin.
      queue.current.clear()
      setSeen(new Set())
      setEnd(false)
      setItems([])
      setLoading(true)
      loadPage(0)
    } catch (e) { toast(errText(e), true) }
  }

  return (
    <div className="card" style={{ marginBottom: 16 }}>
      <div className="card-h">
        <h2>Eslatmalar</h2>
        <span className="hint">{unread ? `${unread} ta o'qilmagan` : "hammasi o'qilgan"}</span>
        <div className="r">
          <div className="seg">
            {([['all', 'Hammasi'], ['Realizatsiya', 'Realizatsiya'],
               ['Perehod', 'Perehod'], ['Admin', 'Tizim']] as const).map(([v, l]) => (
              <button key={v} aria-pressed={role === v} onClick={() => setRole(v)}>{l}</button>
            ))}
          </div>
          <button className="btn sm" disabled={!unread} onClick={markAll}>
            Hammasini o'qildi
          </button>
        </div>
      </div>

      <div>
        {shown.length === 0 && <div className="empty">
          {role === 'all' ? 'Eslatma yo\'q' : 'Bu rol uchun eslatma yo\'q'}
        </div>}
        {shown.map(n => {
          const isRead = n.isRead || seen.has(n.id)
          return (
            <div key={n.id}
              data-id={n.id}
              ref={n.isRead ? undefined : watch}
              style={{
                // Har eslatma to'q chiziq bilan ajratiladi — ro'yxatda
                // ular bir-biriga qo'shilib ketmasin.
                display: 'flex', gap: 12, padding: '13px 16px',
                borderBottom: '2px solid var(--grid)',
                alignItems: 'flex-start', cursor: 'pointer',
                background: isRead ? undefined : 'var(--surface-3)',
                transition: 'background .35s ease',
              }}
              onClick={() => {
                if (!isRead) markSeen(n.id)
                if (n.dealId) nav(`/deals/${n.dealId}`)
              }}>
              <div style={{
                width: 3, borderRadius: 2, alignSelf: 'stretch', flex: 'none',
                background: n.severity === 'Critical' ? 'var(--crit)'
                  : n.severity === 'Warning' ? 'var(--warn)' : 'var(--accent)',
                opacity: isRead ? .45 : 1,
                transition: 'opacity .35s ease',
              }} />
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{
                  fontWeight: isRead ? 500 : 600, fontSize: 13.5,
                  display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap',
                }}>
                  {n.title}
                  {n.targetRole && <span className="role-tag">{ROLE_UZ[n.targetRole]}</span>}
                </div>
                <div style={{ fontSize: 12.5, color: 'var(--ink-2)', marginTop: 2, whiteSpace: 'pre-line' }}>
                  {n.body}
                </div>
              </div>
              <div style={{ fontSize: 11.5, color: 'var(--ink-3)', whiteSpace: 'nowrap' }}>
                {dateTimeUz(n.createdAt)}
              </div>
            </div>
          )
        })}

        {/* Ro'yxat oxiri: keyingi sahifani chaqiradigan ko'rinmas belgi */}
        {!end && items.length > 0 && (
          <div ref={tailRef} style={{
            padding: '14px 16px', textAlign: 'center',
            fontSize: 12, color: 'var(--ink-3)',
          }}>
            {busy ? 'yuklanmoqda…' : ''}
          </div>
        )}

        {end && items.length > 0 && (
          <div style={{
            padding: '14px 16px', textAlign: 'center',
            fontSize: 12, color: 'var(--ink-3)',
          }}>
            — ro'yxat oxiri —
          </div>
        )}
      </div>
    </div>
  )
}
