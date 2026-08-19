/**
 * BIGANT belgisi.
 *
 * Ism «big ant» — katta chumoli. Chumoli o'z og'irligidan bir necha barobar
 * ko'p yuk tashiydi; ulgurji savdo firmasi uchun to'g'ri keladigan obraz.
 *
 * YON ko'rinish tanlangan: ustdan qaralganda chumoli o'rgimchakka o'xshab
 * qoladi, yondan esa bosh–ko'krak–qorin siluети darhol o'qiladi. Belgi 26px
 * da ham, 512px da ham (Telegram profili) bir xil ishlashi kerak.
 */
export function AntMark({ size = 26, color = 'currentColor' }: { size?: number; color?: string }) {
  return (
    <svg width={size} height={size} viewBox="0 0 100 100" fill="none"
      aria-hidden="true" focusable="false">
      {/* Oyoqlar — ko'krakdan pastga, tana ostida qoladi */}
      <g stroke={color} strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round">
        <path d="M41 58 L26 72 L17 66" />
        <path d="M46 60 L42 82 L33 80" />
        <path d="M51 58 L60 80 L55 88" />
        {/* Mo'ylov */}
        <path d="M26 40 L16 30 L8 32" strokeWidth="2.3" />
      </g>

      {/* Tana: bosh — ko'krak — qorin. Qorin eng katta: chumolining belgisi. */}
      <ellipse cx="30" cy="46" rx="11" ry="10" fill={color} />
      <ellipse cx="47" cy="51" rx="9" ry="8" fill={color} />
      <ellipse cx="72" cy="54" rx="19" ry="16" fill={color} />
    </svg>
  )
}

/**
 * To'liq logotip: belgi + so'z.
 * `sub` — belgi ostidagi kichik yozuv (rol nomi).
 */
export function Logo({ sub }: { sub?: string }) {
  return (
    <div className="logo">
      <div className="logo-mark"><AntMark size={26} color="#fff" /></div>
      <div className="logo-text">
        <div className="logo-name">BIGANT</div>
        {sub && <div className="logo-sub">{sub}</div>}
      </div>
    </div>
  )
}
