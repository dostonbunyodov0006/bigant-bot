import { useEffect, useState } from 'react'
import { api, type Readiness } from '../lib/api'
import { ErrorBox, Field, Loading, Modal, errText, useAsync, useToast } from '../lib/ui'

interface Company {
  exists: boolean
  id?: number
  name?: string; tin?: string
  account?: string; bankId?: string; bankName?: string
  address?: string; oked?: number | null
  directorName?: string; directorPinfl?: string
  accountantName?: string; accountantPinfl?: string
  vatRegCode?: string; warehouseAddress?: string
  taxpayerType?: number
  readyForDidox?: boolean
  missing: string[]
}

interface Employee {
  id: number; fullName: string; position: string | null; pinfl: string | null
  phone: string | null; isDriver: boolean; driverLicense: string | null
  canReceiveGoods: boolean; problem: string | null
}

interface Vehicle {
  id: number; model: string; plateNumber: string
  trailerNumber: string | null; capacityTons: number | null; carrierName: string | null
}

const FIELDS: { k: keyof Company; label: string; req?: boolean; hint?: string; full?: boolean }[] = [
  { k: 'name', label: 'Tashkilot nomi', req: true, full: true },
  { k: 'tin', label: 'INN (9 raqam)', req: true },
  { k: 'directorName', label: 'Direktor F.I.Sh', req: true, hint: 'Didox majburiy talab qiladi' },
  { k: 'directorPinfl', label: 'Direktor PINFL (14)', req: true, hint: 'dogovor va doverennost uchun' },
  { k: 'accountantName', label: 'Bosh buxgalter F.I.Sh' },
  { k: 'accountantPinfl', label: 'Buxgalter PINFL' },
  { k: 'account', label: 'Hisob raqami (20)', req: true },
  { k: 'bankId', label: 'Bank MFO (5)', req: true },
  { k: 'bankName', label: 'Bank nomi' },
  { k: 'oked', label: 'OKED', req: true, hint: 'Dogovor NK uchun' },
  { k: 'vatRegCode', label: 'QQS ro\'yxat kodi' },
  { k: 'address', label: 'Yuridik manzil', req: true, full: true },
  { k: 'warehouseAddress', label: 'Ombor manzili', hint: 'TTN yuk ortish nuqtasi', full: true },
]

export default function Setup() {
  const toast = useToast()
  const ready = useAsync(() => api.get<Readiness>('/api/didox-readiness'), [])
  const co = useAsync(() => api.get<Company>('/api/company'), [])
  const emps = useAsync(() => api.get<Employee[]>('/api/employees'), [])
  const vehs = useAsync(() => api.get<Vehicle[]>('/api/vehicles'), [])

  const [f, setF] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const [empForm, setEmpForm] = useState(false)
  const [vehForm, setVehForm] = useState(false)

  useEffect(() => {
    if (!co.data) return
    const init: Record<string, string> = {}
    const src = co.data as unknown as Record<string, unknown>
    for (const x of FIELDS) init[x.k] = String(src[x.k] ?? '')
    setF(init)
  }, [co.data])

  if (co.loading) return <Loading />
  if (co.error) return <ErrorBox text={co.error} />

  const save = async () => {
    setBusy(true)
    try {
      const r = await api.post<{ readyForDidox: boolean; missing: string[] }>('/api/company', {
        ...f,
        oked: f.oked ? Number(f.oked) : null,
        taxpayerType: co.data?.taxpayerType ?? 10,
      })
      toast(r.missing.length
        ? `Saqlandi. Hali ${r.missing.length} ta majburiy maydon bo'sh`
        : "Rekvizitlar to'liq — Didox uchun tayyor")
      co.reload(); ready.reload()
    } catch (e) {
      toast(errText(e), true)
    } finally {
      setBusy(false)
    }
  }

  const r = ready.data
  const blockers = r
    ? r.company.missing.length + r.products.notReady + r.counterparties.notReady
      + (r.employees.canReceiveGoods ? 0 : 1) + (r.employees.drivers ? 0 : 1)
    : 0

  return (
    <>
      <div className="card" style={{ marginBottom: 16 }}>
        <div className="card-h">
          <h2>Didox tayyorligi</h2>
          <span className="hint">yoqishdan oldin shular to'ldirilishi kerak</span>
          <div className="r">
            <span className={'pill ' + (blockers ? 'crit' : 'ok')}>
              {blockers ? `${blockers} ta to'siq` : 'Tayyor'}
            </span>
          </div>
        </div>
        <div style={{ padding: '14px 16px' }}>
          {r && (
            <>
              <Chk ok={r.company.missing.length === 0}
                text={r.company.missing.length
                  ? <><b>Bigant rekvizitlari</b> — yetishmaydi: {r.company.missing.join(', ')}</>
                  : <><b>Bigant rekvizitlari</b> to'liq</>} />
              <Chk ok={r.products.notReady === 0}
                text={r.products.notReady
                  ? <><b>Tovar katalogi</b> — {r.products.notReady} ta tovarda muammo
                    {r.products.sample.length > 0 && <>: {r.products.sample.slice(0, 3).map(p => p.name).join(', ')}…</>}</>
                  : <><b>Tovar katalogi</b> ({r.products.total} ta) — kodlar joyida</>} />
              <Chk ok={r.counterparties.notReady === 0}
                text={r.counterparties.notReady
                  ? <><b>Kontragentlar</b> — {r.counterparties.notReady} tasida kamchilik
                    {r.counterparties.sample.length > 0 &&
                      <>: {r.counterparties.sample.slice(0, 2).map(c => `${c.name} (${c.missing.join(', ')})`).join('; ')}</>}</>
                  : <><b>Kontragentlar</b> to'liq</>} />
              <Chk ok={r.employees.canReceiveGoods > 0}
                text={<><b>Ishonchli shaxs</b> — PINFL bilan {r.employees.canReceiveGoods} ta xodim
                  {r.employees.canReceiveGoods === 0 && '. Doverennost chiqarib bo\'lmaydi'}</>} />
              <Chk ok={r.employees.drivers > 0}
                text={<><b>Haydovchi</b> — {r.employees.drivers} ta
                  {r.employees.drivers === 0 && '. TTN chiqarib bo\'lmaydi'}</>} />
              <Chk ok={r.contractTemplates > 0}
                text={<><b>Shartnoma shabloni</b> — {r.contractTemplates} ta</>} />
            </>
          )}
        </div>
      </div>

      <div className="grid2">
        <div className="card">
          <div className="card-h">
            <h2>Bigant rekvizitlari</h2>
            <span className="hint">Didox har hujjatda so'raydi</span>
          </div>
          <div style={{ padding: '14px 16px', display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 12 }}>
            {FIELDS.map(x => (
              <div key={x.k} style={x.full ? { gridColumn: '1/-1' } : undefined}>
                <Field label={x.label} required={x.req} hint={x.hint}>
                  <input className={'inp' + (x.k.toString().includes('inn') || x.k.toString().includes('Pinfl') ? ' mono' : '')}
                    value={f[x.k] ?? ''} onChange={e => setF(s => ({ ...s, [x.k]: e.target.value }))} />
                </Field>
              </div>
            ))}
          </div>
          <div style={{ padding: '0 16px 14px' }}>
            <button className="btn pri" disabled={busy} onClick={save}>
              {busy ? 'Saqlanmoqda…' : 'Saqlash'}
            </button>
          </div>
        </div>

        <div className="col">
          <div className="card">
            <div className="card-h">
              <h2>Xodimlar</h2>
              <span className="hint">doverennost va TTN uchun</span>
              <div className="r"><button className="btn sm" onClick={() => setEmpForm(true)}>+ Xodim</button></div>
            </div>
            <div className="tw">
              <table>
                <thead><tr><th>F.I.Sh</th><th>Lavozim</th><th>PINFL</th><th>Holat</th></tr></thead>
                <tbody>
                  {emps.data?.length === 0 && <tr><td colSpan={4} className="empty">Xodim yo'q</td></tr>}
                  {emps.data?.map(e => (
                    <tr key={e.id} className={'stripe ' + (e.canReceiveGoods ? '' : 'crit')}>
                      <td>{e.fullName}</td>
                      <td style={{ color: 'var(--ink-3)' }}>
                        {e.position ?? '—'}
                        {e.isDriver && <span className="pill neut" style={{ marginLeft: 6 }}>haydovchi</span>}
                      </td>
                      <td className="mono" style={{ fontSize: 12, color: e.pinfl ? 'var(--ink-3)' : 'var(--crit)' }}>
                        {e.pinfl ?? "yo'q"}</td>
                      <td>{e.canReceiveGoods
                        ? <span className="pill ok">tayyor</span>
                        : <span className="pill crit">PINFL kerak</span>}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>

          <div className="card">
            <div className="card-h">
              <h2>Avtomobillar</h2>
              <span className="hint">TTN uchun</span>
              <div className="r"><button className="btn sm" onClick={() => setVehForm(true)}>+ Avtomobil</button></div>
            </div>
            <div className="tw">
              <table>
                <thead><tr><th>Rusumi</th><th>Davlat raqami</th><th>Pritsep</th><th className="r">Quvvat</th></tr></thead>
                <tbody>
                  {vehs.data?.length === 0 && <tr><td colSpan={4} className="empty">Avtomobil yo'q</td></tr>}
                  {vehs.data?.map(v => (
                    <tr key={v.id}>
                      <td>{v.model}</td>
                      <td className="mono" style={{ fontSize: 12 }}>{v.plateNumber}</td>
                      <td className="mono" style={{ fontSize: 12, color: 'var(--ink-3)' }}>{v.trailerNumber ?? '—'}</td>
                      <td className="r num">{v.capacityTons ?? '—'} t</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>

      {empForm && <EmployeeForm onClose={() => setEmpForm(false)}
        onDone={() => { setEmpForm(false); emps.reload(); ready.reload() }} />}
      {vehForm && <VehicleForm onClose={() => setVehForm(false)}
        onDone={() => { setVehForm(false); vehs.reload(); ready.reload() }} />}
    </>
  )
}

const Chk = ({ ok, text }: { ok: boolean; text: React.ReactNode }) => (
  <div style={{ display: 'flex', gap: 9, padding: '5px 0', alignItems: 'flex-start', fontSize: 13.5 }}>
    <span style={{ color: ok ? 'var(--ok)' : 'var(--crit)', fontWeight: 700, width: 15, flex: 'none' }}>
      {ok ? '✓' : '✕'}
    </span>
    <span style={{ color: 'var(--ink-2)' }}>{text}</span>
  </div>
)

function EmployeeForm({ onClose, onDone }: { onClose: () => void; onDone: () => void }) {
  const toast = useToast()
  const [busy, setBusy] = useState(false)
  const [f, setF] = useState({
    fullName: '', position: '', pinfl: '', phone: '', isDriver: false, driverLicense: '',
  })
  const set = (k: keyof typeof f, v: unknown) => setF(s => ({ ...s, [k]: v }))

  const save = async () => {
    if (f.pinfl && f.pinfl.length !== 14) { toast('PINFL 14 ta raqam bo\'lishi kerak', true); return }
    setBusy(true)
    try {
      await api.post('/api/employees', { ...f, passportSeries: null, passportNumber: null,
        passportIssuedBy: null, passportIssuedDate: null })
      toast('Xodim qo\'shildi')
      onDone()
    } catch (e) { toast(errText(e), true) } finally { setBusy(false) }
  }

  return (
    <Modal title="Yangi xodim" onClose={onClose} onOk={save} busy={busy}>
      <div className="full alert info">
        <div>Didox doverennostda <b>PINFL</b> ni majburiy talab qiladi — passport emas.</div>
      </div>
      <Field label="F.I.Sh" required full>
        <input className="inp" value={f.fullName} onChange={e => set('fullName', e.target.value)} />
      </Field>
      <Field label="Lavozim">
        <input className="inp" value={f.position} onChange={e => set('position', e.target.value)}
          placeholder="Ekspeditor / Haydovchi" />
      </Field>
      <Field label="PINFL (14 raqam)" required hint="doverennost uchun">
        <input className="inp mono" value={f.pinfl} maxLength={14}
          onChange={e => set('pinfl', e.target.value.replace(/\D/g, ''))} />
      </Field>
      <Field label="Telefon">
        <input className="inp" value={f.phone} onChange={e => set('phone', e.target.value)} />
      </Field>
      <Field label="Haydovchilik guvohnomasi">
        <input className="inp" value={f.driverLicense} onChange={e => set('driverLicense', e.target.value)} />
      </Field>
      <div className="full" style={{ display: 'flex', gap: 8, alignItems: 'center' }}>
        <input type="checkbox" className="chk" id="drv" checked={f.isDriver}
          onChange={e => set('isDriver', e.target.checked)} />
        <label htmlFor="drv" style={{ fontSize: 13 }}>Haydovchi — TTN ro'yxatida chiqadi</label>
      </div>
    </Modal>
  )
}

function VehicleForm({ onClose, onDone }: { onClose: () => void; onDone: () => void }) {
  const toast = useToast()
  const [busy, setBusy] = useState(false)
  const [f, setF] = useState({ model: '', plateNumber: '', trailerNumber: '', capacityTons: '', carrierName: '' })
  const set = (k: keyof typeof f, v: string) => setF(s => ({ ...s, [k]: v }))

  const save = async () => {
    setBusy(true)
    try {
      await api.post('/api/vehicles', {
        model: f.model, plateNumber: f.plateNumber,
        trailerNumber: f.trailerNumber || null,
        capacityTons: f.capacityTons ? Number(f.capacityTons) : null,
        carrierName: f.carrierName || null, carrierInn: null,
      })
      toast('Avtomobil qo\'shildi')
      onDone()
    } catch (e) { toast(errText(e), true) } finally { setBusy(false) }
  }

  return (
    <Modal title="Yangi avtomobil" onClose={onClose} onOk={save} busy={busy}>
      <Field label="Rusumi" required full>
        <input className="inp" value={f.model} onChange={e => set('model', e.target.value)}
          placeholder="Isuzu NQR 75" />
      </Field>
      <Field label="Davlat raqami" required>
        <input className="inp mono" value={f.plateNumber} onChange={e => set('plateNumber', e.target.value)}
          placeholder="01 A 123 BC" />
      </Field>
      <Field label="Pritsep raqami">
        <input className="inp mono" value={f.trailerNumber} onChange={e => set('trailerNumber', e.target.value)} />
      </Field>
      <Field label="Yuk ko'tarish, tonna">
        <input className="inp num" type="number" step="0.1" value={f.capacityTons}
          onChange={e => set('capacityTons', e.target.value)} />
      </Field>
      <Field label="Tashuvchi tashkilot" hint="o'zimizniki bo'lsa bo'sh qoldiring">
        <input className="inp" value={f.carrierName} onChange={e => set('carrierName', e.target.value)} />
      </Field>
    </Modal>
  )
}
