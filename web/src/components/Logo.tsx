/**
 * Yon paneldagi firma belgisi.
 *
 * Rasm tafsilotli — chelak, quvurlar, vintlar va chumoli. Uni 40 pikselli
 * kvadratga siqish ham, bir qismini kesib olish ham ish bermadi: birinchisida
 * loy bo'lib qoladi, ikkinchisida yarmi ko'rinmaydi.
 *
 * Shuning uchun rasm panel kengligida, TO'LIQ ko'rsatiladi. «BIGANT» yozuvi
 * rasmning o'zida bor — uni yana matn bilan takrorlash shart emas.
 *
 * `sub` — rasm ostidagi rol nomi.
 */
export function Logo({ sub }: { sub?: string }) {
  return (
    <div className="logo">
      <img className="logo-img" src="/bigant-logo.jpg" alt="Bigant" />
      {sub && <div className="logo-sub">{sub}</div>}
    </div>
  )
}
