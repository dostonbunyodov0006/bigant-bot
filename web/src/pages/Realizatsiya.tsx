import { useNavigate } from 'react-router-dom'
import { api, money, dateUz } from '../lib/api'
import { ErrorBox, Loading, useAsync } from '../lib/ui'
import { useLive } from '../lib/live'
import { Kpi, Work } from '../components/RoleUi'
import { fold, Search, StatusFilter, useSort } from '../components/TableSort'
import { useState } from 'react'

interface DealRow {
  id: number; number: string; counterparty: string; inn: string | null
  amountTotal: number; amountPaid: number; debt: number; vat: number
  status: string; contractDate: string | null; dueDate: string | null
  hasEsf: boolean; hasTtn: boolean
}

interface View {
  summary: {
    openDeals: number; closedDeals: number
    soldTotal: number; receivedTotal: number; receivable: number; vatTotal: number
  }
  checks: {
    withoutInn: string[]
    overduePayments: { number: string; counterparty: string; debt: number; lateDays: number }[]
    withoutEsf: { number: string; counterparty: string; amountTotal: number }[]
  }
  deals: DealRow[]
}

export default function Realizatsiya() {
  const nav = useNavigate()
  const { data, loading, error, reload } = useAsync<View>(
    () => api.get('/api/view/realizatsiya'), [], 60000)

  // Tugagan shartnomalar davom etayotganlaridan ajratilsin.
  const [filter, setFilter] = useState<'open' | 'closed' | 'all'>('open')

  // Qidiruv: MChJ nomi, shartnoma raqami yoki INN bo'yicha.
  const [q, setQ] = useState('')

  const all = data?.deals ?? []
  const needle = fold(q)
  const shown = all
    .filter(d => filter === 'all' ? true
      : filter === 'closed' ? d.status === 'Closed' : d.status !== 'Closed')
    .filter(d => !needle
      || fold(d.counterparty).includes(needle)
      || fold(d.number).includes(needle)
      || (d.inn ?? '').includes(needle))

  // Sukut bo'yicha eng katta qarz yuqorida — ko'z birinchi shunga tushsin.
  const { rows: deals, th, mark } = useSort(shown, 'debt')

  // Jonli oqim: server yangi xabar chiqarishi bilan ekran yangilanadi.
  // 60 soniyalik so'rov — zaxira, aloqa uzilib qolgan holat uchun.
  useLive(() => reload())

  if (loading) return <Loading />
  if (error) return <ErrorBox text={error} />
  if (!data) return null

  const { summary: s, checks: c } = data

  // Muddati o'tganini shu bilan solishtiramiz (YYYY-MM-DD ko'rinishida
  // matn taqqoslash sana taqqoslash bilan bir xil natija beradi).
  const bugun = new Date().toISOString().slice(0, 10)

  // «Sizning ishingiz» — hamma tekshiruv bitta ro'yxatga yig'iladi,
  // eng shoshilinchi yuqorida.
  const work = [
    ...c.overduePayments.map(o => ({
      key: 'pay' + o.number,
      text: `${o.counterparty} — to'lov kelmadi`,
      sub: `${o.number} · muddat ${o.lateDays} kun oldin o'tgan`,
      right: money(o.debt),
      tone: 'crit' as const,
    })),
    ...c.withoutInn.map(n => ({
      key: 'inn' + n, text: `${n} — kontragentda INN yo'q`,
      sub: 'Didox INN siz faktura qabul qilmaydi',
    })),
    // Qatorda MChJ nomi va summa bo'lsin: faqat shartnoma raqami
    // yozilganda ro'yxatdagi hamma qator bir xil ko'rinardi.
    ...c.withoutEsf.map(e => ({
      key: 'esf' + e.number,
      text: `${e.counterparty} — schyot-faktura chiqarilmagan`,
      sub: `${e.number} · tovar berilgan, sotuv hali rasmiylashmagan`,
      right: money(e.amountTotal),
    })),
  ]

  return (
    <>

      <div className="kpis">
        {/* Yozuvlar raqamning ASL mazmunini aytadi. Ilgari «Jami sotuv
            summasi» tagida «68 davom etayotgan shartnoma» derdi, lekin
            raqam 77 ta shartnomani (tugaganlarini ham) qamrab olardi.
            «Bizga tushgan pul» tagida «30 kun ichida» derdi, aslida esa
            shu shartnomalar bo'yicha TO'LIQ tushgan pul edi. */}
        <Kpi label="Jami sotuv summasi" value={money(s.soldTotal)}
          note={`${s.openDeals} davom etayotgan + ${s.closedDeals} tugagan shartnoma`} />
        <Kpi label="Bizga tushgan pul" value={money(s.receivedTotal)} tone="ok"
          note="shu shartnomalar bo'yicha jami" />
        <Kpi label="Klientlar bizga qarzdor" value={money(s.receivable)}
          tone={s.receivable > 0 ? 'warn' : 'ok'} note="hali olinmagan pul" />
        <Kpi label="Sotuvdan QQS" value={money(s.vatTotal)}
          note="chiqarilgan fakturalar bo'yicha" />
        <Kpi label="Tugagan shartnomalar" value={s.closedDeals} tone="ok"
          note="oxirgi 30 kunda yopilgan" />
      </div>

      <Work title="Tekshirilishi kerak bo'lgan shartnomalar" empty="Hamma sotuv hujjati joyida"
        items={work} />

      <div className="card">
        <div className="card-h">
          <h2>Sotuv shartnomalari</h2>
          <div className="r">
            <Search value={q} onChange={setQ} />
            <StatusFilter value={filter} onChange={setFilter} counts={{
              open: all.filter(d => d.status !== 'Closed').length,
              closed: all.filter(d => d.status === 'Closed').length,
            }} />
          </div>
        </div>
        <div className="tw">
          <table>
            <thead>
              <tr>
                <th {...th('number')}>Shartnoma raqami{mark('number')}</th>
                <th {...th('counterparty')}>Kimga sotildi (MChJ){mark('counterparty')}</th>
                <th>INN</th>
                <th {...th('amountTotal')} className={th('amountTotal').className + ' r'}>Shartnoma summasi{mark('amountTotal')}</th>
                <th {...th('amountPaid')} className={th('amountPaid').className + ' r'}>Bizga tushgan{mark('amountPaid')}</th>
                <th {...th('debt')} className={th('debt').className + ' r'}>Qolgan qarz{mark('debt')}</th>
                <th {...th('dueDate')}>To'lov muddati{mark('dueDate')}</th>
                <th>Hujjatlari bormi</th>
                <th {...th('status')}>Shartnoma holati{mark('status')}</th>
              </tr>
            </thead>
            <tbody>
              {deals.length === 0 && <tr><td colSpan={9} className="empty">
                {q ? `«${q}» topilmadi`
                  : filter === 'closed' ? 'Tugagan shartnoma yo\'q' : 'Davom etayotgan sotuv shartnomasi yo\'q'}
              </td></tr>}
              {deals.map(d => (
                <tr key={d.id} className="click" tabIndex={0}
                  onClick={() => nav(`/deals/${d.id}`)}
                  onKeyDown={e => { if (e.key === 'Enter') nav(`/deals/${d.id}`) }}>
                  <td className="mono" style={{ fontWeight: 500 }}>{d.number}
                    <div style={{ fontSize: 11, color: 'var(--ink-3)' }}>{dateUz(d.contractDate)}</div>
                  </td>
                  <td>{d.counterparty}
                  </td>
                  <td className="mono" style={{ fontSize: 12, color: d.inn ? 'var(--ink-3)' : 'var(--crit)' }}>
                    {d.inn ?? "yo'q ⚠"}
                  </td>
                  <td className="r num">{money(d.amountTotal)}</td>
                  <td className="r num" style={{ color: 'var(--ok)' }}>{money(d.amountPaid)}</td>
                  <td className="r num" style={{
                    color: d.debt > 0 ? 'var(--crit)' : 'var(--ok)',
                    fontWeight: d.debt > 0 ? 600 : 400,
                  }}>{money(d.debt)}</td>
                  {/* To'lov muddati serverdan kelardi, lekin ekranda ustuni
                      yo'q edi — buxgalter qaysi pul kechikkanini jadvaldan
                      ko'ra olmasdi. Qarzi bor va muddati o'tgan bo'lsa qizil. */}
                  <td style={{ fontSize: 12 }}>
                    {d.dueDate
                      ? <span style={{
                          color: d.debt > 0 && d.dueDate < bugun ? 'var(--crit)' : 'var(--ink-2)',
                          fontWeight: d.debt > 0 && d.dueDate < bugun ? 600 : 400,
                        }}>
                          {dateUz(d.dueDate)}
                          {d.debt > 0 && d.dueDate < bugun && " — o'tgan"}
                        </span>
                      : <span style={{ color: 'var(--ink-3)' }}>ko'rsatilmagan</span>}
                  </td>
                  <td>
                    <div style={{ display: 'flex', gap: 4 }}>
                      <span className={'pill ' + (d.hasTtn ? 'ok' : 'off')} title="TTN">TTN</span>
                      <span className={'pill ' + (d.hasEsf ? 'ok' : 'off')} title="Schyot-faktura">ESF</span>
                    </div>
                  </td>
                  <td>
                    <span className={'pill ' + (d.status === 'Closed' ? 'ok' : 'warn')}>
                      {d.status === 'Closed' ? 'Tugagan' : 'Davom etyapti'}
                    </span>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </>
  )
}
