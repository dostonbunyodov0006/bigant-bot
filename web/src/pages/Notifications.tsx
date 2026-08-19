import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, dateTimeUz, type Notif } from '../lib/api'
import { ErrorBox, Loading, Modal, errText, useAsync, useToast } from '../lib/ui'

export default function Notifications() {
  const nav = useNavigate()
  const toast = useToast()
  const [tg, setTg] = useState<{ code: string; link: string | null; command: string } | null>(null)

  const { data, loading, error, reload } = useAsync(
    () => api.get<{ unread: number; items: Notif[] }>('/api/notifications?take=100'), [])
  const status = useAsync(
    () => api.get<{ connected: boolean; username: string | null; muted: boolean }>(
      '/api/notifications/telegram/status'), [])

  if (loading) return <Loading />
  if (error) return <ErrorBox text={error} />
  if (!data) return null

  const markRead = async (ids: number[]) => {
    try { await api.post('/api/notifications/read', { ids }); reload() }
    catch (e) { toast(errText(e), true) }
  }

  const connect = async () => {
    try {
      const r = await api.post<{ code: string; link: string | null; command: string }>(
        '/api/notifications/telegram/code')
      setTg(r)
    } catch (e) { toast(errText(e), true) }
  }

  const disconnect = async () => {
    try { await api.del('/api/notifications/telegram'); toast('Telegram uzildi'); status.reload() }
    catch (e) { toast(errText(e), true) }
  }

  return (
    <>
      <div className="card" style={{ marginBottom: 16 }}>
        <div className="card-h">
          <h2>Eslatmalar</h2>
          <span className="hint">{data.unread ? `${data.unread} ta o'qilmagan` : "hammasi o'qilgan"}</span>
          <div className="r">
            {status.data?.connected ? (
              <>
                <span className="pill ok">
                  Telegram ulangan{status.data.username ? ` · @${status.data.username}` : ''}
                </span>
                <button className="btn sm" onClick={disconnect}>Uzish</button>
              </>
            ) : (
              <button className="btn sm" onClick={connect}>Telegramga ulash</button>
            )}
            <button className="btn sm" disabled={!data.unread}
              onClick={() => markRead(data.items.filter(n => !n.isRead).map(n => n.id))}>
              Hammasini o'qildi
            </button>
          </div>
        </div>

        <div>
          {data.items.length === 0 && <div className="empty">Eslatma yo'q</div>}
          {data.items.map(n => (
            <div key={n.id}
              style={{
                display: 'flex', gap: 12, padding: '12px 16px', borderBottom: '1px solid var(--line)',
                alignItems: 'flex-start', cursor: 'pointer',
                background: n.isRead ? undefined : 'var(--surface-3)',
              }}
              onClick={() => {
                if (!n.isRead) markRead([n.id])
                if (n.dealId) nav(`/deals/${n.dealId}`)
              }}>
              <div style={{
                width: 3, borderRadius: 2, alignSelf: 'stretch', flex: 'none',
                background: n.severity === 'Critical' ? 'var(--crit)'
                  : n.severity === 'Warning' ? 'var(--warn)' : 'var(--accent)',
              }} />
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ fontWeight: 600, fontSize: 13.5 }}>{n.title}</div>
                <div style={{ fontSize: 12.5, color: 'var(--ink-2)', marginTop: 2, whiteSpace: 'pre-line' }}>
                  {n.body}
                </div>
              </div>
              <div style={{ fontSize: 11.5, color: 'var(--ink-3)', whiteSpace: 'nowrap' }}>
                {dateTimeUz(n.createdAt)}
              </div>
            </div>
          ))}
        </div>
      </div>

      {tg && (
        <Modal title="Telegramga ulash" onClose={() => { setTg(null); status.reload() }}>
          <div className="full alert info">
            <div>Botga quyidagi buyruqni yuboring. Kod <b>15 daqiqa</b> amal qiladi.
              Parol Telegramga hech qachon yuborilmaydi.</div>
          </div>
          <div className="full" style={{ textAlign: 'center', padding: '10px 0' }}>
            <div className="mono" style={{ fontSize: 30, fontWeight: 600, letterSpacing: '.12em' }}>
              {tg.code}
            </div>
            <div className="mono" style={{ fontSize: 13, color: 'var(--ink-3)', marginTop: 6 }}>
              {tg.command}
            </div>
          </div>
          {tg.link && (
            <div className="full" style={{ textAlign: 'center' }}>
              <a className="btn pri" href={tg.link} target="_blank" rel="noreferrer">
                Telegramda ochish
              </a>
            </div>
          )}
        </Modal>
      )}
    </>
  )
}
