import { useNavigate } from 'react-router-dom'
import { api, money, dateUz } from '../lib/api'
import { ErrorBox, Loading, useAsync } from '../lib/ui'
import { useLive } from '../lib/live'
import { Kpi, Work } from '../components/RoleUi'
import { fold, Search } from '../components/TableSort'
import { useState } from 'react'

interface ChainDoc {
  id: number; type: string; label: string; number: string | null
  date: string; direction: string; amount: number
  basis: string | null; amountMatchesParent: boolean
}
interface Chain {
  dealId: number; number: string; kind: string; counterparty: string
  isBroken: boolean; problems: string[]; documents: ChainDoc[]
}
interface Waiting {
  dealId: number; number: string; counterparty: string
  step: string; label: string; weAct: boolean; text: string
  dueDate: string; lateDays: number
}
interface View {
  summary: {
    activeChains: number; brokenChains: number
    waitingSteps: number; lateSteps: number; unlinkedDocs: number
  }
  chains: Chain[]
  waiting: Waiting[]
  unlinked: { id: number; label: string; docNumber: string | null; amount: number }[]
}

export default function Perehod() {
  const nav = useNavigate()
  const { data, loading, error, reload } = useAsync<View>(
    () => api.get('/api/view/perehod'), [], 60000)

  // Jonli oqim: server yangi xabar chiqarishi bilan ekran yangilanadi.
  // 60 soniyalik so'rov — zaxira, aloqa uzilib qolgan holat uchun.
  // Qidiruv — shartnomalar ko'payganda ko'z bilan topib bo'lmaydi.
  const [q, setQ] = useState('')

  useLive(() => reload())

  if (loading) return <Loading />
  if (error) return <ErrorBox text={error} />
  if (!data) return null

  const { summary: s, waiting, unlinked } = data

  // Qidiruv: MChJ nomi yoki shartnoma raqami bo'yicha.
  const needle = fold(q)
  const chains = data.chains.filter(c => !needle
    || fold(c.counterparty).includes(needle)
    || fold(c.number).includes(needle))

  const broken = chains.filter(c => c.isBroken)
  const whole = chains.filter(c => !c.isBroken)

  // Kechikkan qadamlar — ish shu yerda to'xtab qolgan.
  const work = [
    ...waiting.filter(w => w.lateDays > 0).slice(0, 25).map(w => ({
      key: 'w' + w.dealId + w.step,
      text: `${w.counterparty} — ${w.text}`,
      sub: `${w.number} · muddat ${w.lateDays} kun oldin o'tgan (${dateUz(w.dueDate)})`,
      // Kim harakat qilishi kerakligi aniq yozilsin: «bizda / ularda»
      // degan qisqa so'z nima qilish kerakligini aytmasdi.
      right: w.weAct ? 'Biz yuboramiz' : 'Ular yuboradi',
      tone: w.lateDays > 7 ? ('crit' as const) : undefined,
    })),
    ...unlinked.map(u => ({
      key: 'u' + u.id,
      text: `${u.label} №${u.docNumber ?? '—'} — qaysi shartnomaga tegishli ekani noma'lum`,
      sub: 'Qaysi shartnomaga tegishli ekanini aniqlash kerak',
      right: money(u.amount),
      tone: 'crit' as const,
    })),
  ]

  return (
    <>
      {/* Qidiruv eng tepada — shartnomalar ko'payganda birinchi shu kerak bo'ladi. */}
      <div className="filters" style={{ marginBottom: 14 }}>
        <Search value={q} onChange={setQ} />
        <span style={{ fontSize: 12.5, color: 'var(--ink-3)' }}>{chains.length} ta shartnoma</span>
      </div>

      <div className="kpis">
        <Kpi label="Hujjatlari to'liq shartnomalar" value={s.activeChains} note="ochiq shartnomalar ichida" />
        <Kpi label="Hujjati to'liq emas shartnomalar" value={s.brokenChains}
          tone={s.brokenChains > 0 ? 'crit' : 'ok'} note="darhol tekshirish kerak" />
        <Kpi label="Kelishi kutilayotgan hujjatlar" value={s.waitingSteps} note="hali kelmagan yoki yuborilmagan hujjat" />
        <Kpi label="Muddati o'tgan hujjatlar" value={s.lateSteps}
          tone={s.lateSteps > 0 ? 'warn' : 'ok'} note="shu yerda to'xtab qolgan" />
        <Kpi label="Shartnomasi aniqlanmagan hujjatlar" value={s.unlinkedDocs}
          tone={s.unlinkedDocs > 0 ? 'crit' : 'ok'} note="qaysi shartnomaga tegishli noma'lum" />
      </div>

      <Work title="To'xtab qolgan ishlar"
        empty="Barcha hujjat o'z vaqtida harakatlanmoqda" items={work} />

      {broken.length > 0 && (
        <div className="card" style={{ marginBottom: 16 }}>
          <div className="card-h">
            <h2>Hujjati to'liq emas shartnomalar</h2>
            <span className="pill crit">{broken.length} ta shartnoma</span>
          </div>
          <div style={{ padding: 14 }}>
            {broken.map(c => <ChainRow key={c.dealId} c={c} onOpen={() => nav(`/deals/${c.dealId}`)} />)}
          </div>
        </div>
      )}

      <div className="card">
        <div className="card-h">
          <h2>Hujjatlari to'liq shartnomalar</h2>
          <span className="hint">{whole.length} ta shartnoma — hujjatlari to'g'ri bog'langan</span>
        </div>
        <div style={{ padding: 14 }}>
          {whole.length === 0
            ? <div className="empty">{q ? `«${q}» topilmadi` : 'Hujjatlari to\'liq shartnoma hali yo\'q'}</div>
            : whole.slice(0, 30).map(c =>
                <ChainRow key={c.dealId} c={c} onOpen={() => nav(`/deals/${c.dealId}`)} />)}
        </div>
      </div>
    </>
  )
}

/** Bitta shartnomaning hujjatlari — ketma-ketlik bo'yicha. */
function ChainRow({ c, onOpen }: { c: Chain; onOpen: () => void }) {
  return (
    <div className={'chain-row' + (c.isBroken ? ' broken' : '')}>
      <div className="chain-head">
        <button className="btn sm" onClick={onOpen}>{c.number}</button>
        <b style={{ fontSize: 13.5 }}>{c.counterparty}</b>
        <span className={'pill ' + (c.kind === 'Sale' ? 'acc' : 'neut')}>
          {c.kind === 'Sale' ? 'Sotuv' : 'Xarid'}
        </span>
        {!c.isBroken && <span className="pill ok" style={{ marginLeft: 'auto' }}>✓ hujjatlari to'liq</span>}
      </div>

      <div className="chain-flow">
        {c.documents.map((d, i) => (
          <span key={d.id} style={{ display: 'inline-flex', alignItems: 'center', gap: 5 }}>
            {i > 0 && <span className="arrow">→</span>}
            <span className={'chip' + (d.direction === 'Out' ? ' us' : '')}
              title={`${d.direction === 'Out' ? 'Biz yubordik' : 'Bizga keldi'} · ${dateUz(d.date)} · ${money(d.amount)}`}>
              {d.direction === 'Out' ? '↑' : '↓'} {d.label}
              <span className="no">{d.number ?? '—'}</span>
              {d.basis && !d.amountMatchesParent &&
                <span title="Summa oldingi hujjatdan farq qiladi" style={{ color: 'var(--warn)' }}>≠</span>}
            </span>
          </span>
        ))}
      </div>

      {c.isBroken && (
        <div className="chain-warn">
          {c.problems.map(p => <div key={p}>⚠️ {p}</div>)}
        </div>
      )}
    </div>
  )
}
