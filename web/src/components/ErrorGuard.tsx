import { Component, type ErrorInfo, type ReactNode } from 'react'

/**
 * XATO TO'SIQ.
 *
 * React'da sahifa chizilayotganda kutilmagan xato chiqsa, u butun daraxtni
 * o'chirib tashlaydi — ekran OPPOQ bo'lib qoladi. Foydalanuvchi nima
 * bo'lganini bilmaydi, biz ham bilmaymiz.
 *
 * Bu to'siq xatoni ushlab qoladi: sahifaning qolgan qismi (chap panel,
 * menyu) joyida turadi, xato esa o'qiladigan holda ko'rsatiladi.
 * Shunda bugalter «ortga» qaytib ishini davom ettira oladi.
 */
interface State { error: Error | null }

export class ErrorGuard extends Component<{ children: ReactNode }, State> {
  state: State = { error: null }

  static getDerivedStateFromError(error: Error): State {
    return { error }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // Konsolga to'liq yozamiz — kerak bo'lsa shu yerdan o'qiladi.
    console.error('[Bigant] sahifa chizilmadi:', error, info.componentStack)
  }

  render() {
    if (!this.state.error) return this.props.children

    return (
      <div className="card" style={{ padding: 20, maxWidth: 680 }}>
        <h2 style={{ fontSize: 15, marginBottom: 8 }}>Bu sahifani ochib bo'lmadi</h2>
        <p style={{ fontSize: 13, color: 'var(--ink-2)', margin: '0 0 14px', lineHeight: 1.6 }}>
          Dasturda kutilmagan xato yuz berdi. Ma'lumotlaringiz joyida —
          faqat shu ekran chizilmadi.
        </p>

        {/* Texnik matn yopiq turadi: bugalterga u hech narsa anglatmaydi.
            Kerak bo'lganda ochib, dasturchiga ko'rsatish mumkin. */}
        <details style={{ marginBottom: 14 }}>
          <summary style={{
            cursor: 'pointer', fontSize: 12, color: 'var(--ink-3)', userSelect: 'none',
          }}>Texnik tafsilot</summary>
          <pre style={{
            background: 'var(--surface-3)', border: '1px solid var(--line)',
            borderRadius: 6, padding: '10px 12px', fontSize: 12,
            color: 'var(--ink-2)', overflowX: 'auto', margin: '8px 0 0',
          }}>{this.state.error.message}</pre>
        </details>

        <div style={{ display: 'flex', gap: 8 }}>
          <button className="btn sm" onClick={() => history.back()}>← Ortga</button>
          <button className="btn sm pri" onClick={() => location.reload()}>Qayta yuklash</button>
        </div>
      </div>
    )
  }
}
