import { useNavigate } from 'react-router-dom'
import { api, money, dateUz } from '../lib/api'
import { ErrorBox, Loading, useAsync } from '../lib/ui'
import { Explain, Kpi, Work } from '../components/RoleUi'

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
    withoutEsf: string[]
  }
  deals: DealRow[]
}

export default function Realizatsiya() {
  const nav = useNavigate()
  const { data, loading, error } = useAsync<View>(() => api.get('/api/view/realizatsiya'), [], 15000)

  if (loading) return <Loading />
  if (error) return <ErrorBox text={error} />
  if (!data) return null

  const { summary: s, checks: c, deals } = data

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
    ...c.withoutEsf.map(n => ({
      key: 'esf' + n, text: `${n} — schyot-faktura chiqarilmagan`,
      sub: 'Sotuv rasmiylashmagan',
    })),
  ]

  return (
    <>
      <Explain mark="R" title="Realizatsiya buxgalteriyasi"
        can={['Sotuv bitimlari', 'Faktura va TTN nazorati', 'INN tekshiruvi',
              'QQS', 'Tushgan pul', 'Debitor qarzdorlik']}>
        Bu ekran bitta savolga javob beradi: <b>nima sotdik, kimga, qancha summaga
        va puli keldimi.</b> Faqat sotuv bitimlari ko'rinadi — xarid boshqa
        buxgalterning ishi. Quyidagi «Tekshirish kerak» ro'yxati sizning kunlik
        ish rejangiz: har qatorni hal qilsangiz ro'yxat qisqaradi.
      </Explain>

      <div className="kpis">
        <Kpi label="Sotildi" value={money(s.soldTotal)} note={`${s.openDeals} ochiq bitim`} />
        <Kpi label="Pul tushdi" value={money(s.receivedTotal)} tone="ok" note="30 kun ichida" />
        <Kpi label="Bizga qarzdor" value={money(s.receivable)}
          tone={s.receivable > 0 ? 'warn' : 'ok'} note="to'lanmagan qoldiq" />
        <Kpi label="QQS" value={money(s.vatTotal)} note="soliqqa tegishli" />
        <Kpi label="Yopilgan" value={s.closedDeals} tone="ok" note="hujjatlari to'liq" />
      </div>

      <Work title="Tekshirish kerak" empty="Hamma sotuv hujjati joyida — tekshiriladigan narsa yo'q"
        items={work} />

      <div className="card">
        <div className="card-h">
          <h2>Sotuv bitimlari</h2>
          <span className="hint">qatorni bosing — to'liq kartochka ochiladi</span>
        </div>
        <div className="tw">
          <table>
            <thead>
              <tr>
                <th>Shartnoma</th><th>Kimga sotildi</th><th>INN</th>
                <th className="r">Summa</th><th className="r">Tushdi</th><th className="r">Qarz</th>
                <th>Hujjatlar</th><th>Holat</th>
              </tr>
            </thead>
            <tbody>
              {deals.length === 0 && <tr><td colSpan={8} className="empty">Sotuv bitimi yo'q</td></tr>}
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
                  <td>
                    <div style={{ display: 'flex', gap: 4 }}>
                      <span className={'pill ' + (d.hasTtn ? 'ok' : 'neut')} title="TTN">TTN</span>
                      <span className={'pill ' + (d.hasEsf ? 'ok' : 'neut')} title="Schyot-faktura">ESF</span>
                    </div>
                  </td>
                  <td>
                    <span className={'pill ' + (d.status === 'Closed' ? 'ok' : 'warn')}>
                      {d.status === 'Closed' ? 'Yopildi' : 'Ochiq'}
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
