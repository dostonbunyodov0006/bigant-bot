import { useNavigate } from 'react-router-dom'
import { api, money, dateUz } from '../lib/api'
import { ErrorBox, Loading, useAsync } from '../lib/ui'
import { Explain, Kpi, Work } from '../components/RoleUi'

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
  const { data, loading, error } = useAsync<View>(() => api.get('/api/view/perehod'), [], 15000)

  if (loading) return <Loading />
  if (error) return <ErrorBox text={error} />
  if (!data) return null

  const { summary: s, chains, waiting, unlinked } = data
  const broken = chains.filter(c => c.isBroken)
  const whole = chains.filter(c => !c.isBroken)

  // Kechikkan qadamlar — zanjir shu yerda to'xtab qolgan.
  const work = [
    ...waiting.filter(w => w.lateDays > 0).slice(0, 25).map(w => ({
      key: 'w' + w.dealId + w.step,
      text: `${w.counterparty} — ${w.text}`,
      sub: `${w.number} · muddat ${w.lateDays} kun oldin o'tgan (${dateUz(w.dueDate)})`,
      right: w.weAct ? '📤 bizda' : '📥 ularda',
      tone: w.lateDays > 7 ? ('crit' as const) : undefined,
    })),
    ...unlinked.map(u => ({
      key: 'u' + u.id,
      text: `${u.label} №${u.docNumber ?? '—'} — hech qaysi shartnomaga ilinmagan`,
      sub: 'Qaysi bitimga tegishli ekanini aniqlash kerak',
      right: money(u.amount),
      tone: 'crit' as const,
    })),
  ]

  return (
    <>
      <Explain mark="P" title="Perehod buxgalteriyasi"
        can={['Hujjat zanjiri', 'Zanjir uzilishlari', 'Kutilayotgan hujjatlar',
              'Summa mosligi', 'Ilinmagan hujjatlar']}>
        Bu ekran bitta savolga javob beradi: <b>qaysi hujjat qaysi hujjatga
        tegishli va keyingi bosqichga o'tdimi.</b> Zanjir shunday bo'lishi kerak:
        dogovor → doverennost → TTN → faktura. Bir bo'g'in tushib qolsa qizil
        belgi chiqadi — demak tovar hujjatsiz harakat qilgan yoki hujjat
        noto'g'ri bitimga tushgan.
      </Explain>

      <div className="kpis">
        <Kpi label="Faol zanjir" value={s.activeChains} note="ochiq bitimlar" />
        <Kpi label="Zanjir uzilgan" value={s.brokenChains}
          tone={s.brokenChains > 0 ? 'crit' : 'ok'} note="darhol tekshiring" />
        <Kpi label="Kutilayotgan hujjat" value={s.waitingSteps} note="hali kelmagan yoki yuborilmagan" />
        <Kpi label="Muddati o'tgan" value={s.lateSteps}
          tone={s.lateSteps > 0 ? 'warn' : 'ok'} note="zanjir shu yerda to'xtagan" />
        <Kpi label="Ilinmagan hujjat" value={s.unlinkedDocs}
          tone={s.unlinkedDocs > 0 ? 'crit' : 'ok'} note="bitimi topilmagan" />
      </div>

      <Work title="Zanjir shu yerda to'xtagan"
        empty="Barcha hujjat o'z vaqtida harakatlanmoqda" items={work} />

      {broken.length > 0 && (
        <div className="card" style={{ marginBottom: 16 }}>
          <div className="card-h">
            <h2>Uzilgan zanjirlar</h2>
            <span className="pill crit">{broken.length}</span>
            <span className="hint">bu bitimlarda hujjat yetishmayapti</span>
          </div>
          <div style={{ padding: 14 }}>
            {broken.map(c => <ChainRow key={c.dealId} c={c} onOpen={() => nav(`/deals/${c.dealId}`)} />)}
          </div>
        </div>
      )}

      <div className="card">
        <div className="card-h">
          <h2>Butun zanjirlar</h2>
          <span className="hint">{whole.length} ta bitim — hujjatlari to'g'ri bog'langan</span>
        </div>
        <div style={{ padding: 14 }}>
          {whole.length === 0
            ? <div className="empty">Hali butun zanjir yo'q</div>
            : whole.slice(0, 30).map(c =>
                <ChainRow key={c.dealId} c={c} onOpen={() => nav(`/deals/${c.dealId}`)} />)}
        </div>
      </div>
    </>
  )
}

/** Bitta bitimning hujjat zanjiri — o'qdan o'qqa. */
function ChainRow({ c, onOpen }: { c: Chain; onOpen: () => void }) {
  return (
    <div className={'chain-row' + (c.isBroken ? ' broken' : '')}>
      <div className="chain-head">
        <button className="btn sm" onClick={onOpen}>{c.number}</button>
        <b style={{ fontSize: 13.5 }}>{c.counterparty}</b>
        <span className={'pill ' + (c.kind === 'Sale' ? 'acc' : 'neut')}>
          {c.kind === 'Sale' ? 'Sotuv' : 'Xarid'}
        </span>
        {!c.isBroken && <span className="pill ok" style={{ marginLeft: 'auto' }}>✓ butun</span>}
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
