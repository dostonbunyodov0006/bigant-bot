// ═══════════════════════════════════════════════════════════════════
//  BIGANT — bot profil rasmi
//
//  Tashqi kutubxona yo'q, shuning uchun rasm piksel darajasida
//  chiziladi: 4 barobar katta lavhada ishlanadi va keyin kichraytiriladi
//  (supersampling) — chetlari tekis va tiniq chiqadi.
// ═══════════════════════════════════════════════════════════════════
import zlib from 'node:zlib'
import fs from 'node:fs'

const OUT = 512
const SS = 4                 // supersampling darajasi
const W = OUT * SS, H = OUT * SS

// ── Lavha ──
const buf = new Float64Array(W * H * 3)
const alpha = new Float64Array(W * H)

function blend(x, y, r, g, b, a) {
  if (a <= 0 || x < 0 || y < 0 || x >= W || y >= H) return
  const i = (y * W + x)
  const j = i * 3
  const ia = 1 - a
  buf[j]     = buf[j]     * ia + r * a
  buf[j + 1] = buf[j + 1] * ia + g * a
  buf[j + 2] = buf[j + 2] * ia + b * a
  alpha[i] = alpha[i] * ia + a
}

function fillRect(x0, y0, x1, y1, c) {
  for (let y = Math.max(0, y0 | 0); y < Math.min(H, y1 | 0); y++)
    for (let x = Math.max(0, x0 | 0); x < Math.min(W, x1 | 0); x++)
      blend(x, y, c[0], c[1], c[2], c[3] ?? 1)
}

/** Aylanma ellips. `rot` — radian. */
function ellipse(cx, cy, rx, ry, rot, c) {
  const cos = Math.cos(-rot), sin = Math.sin(-rot)
  const rad = Math.max(rx, ry) + 2
  for (let y = Math.max(0, (cy - rad) | 0); y < Math.min(H, (cy + rad) | 0); y++) {
    for (let x = Math.max(0, (cx - rad) | 0); x < Math.min(W, (cx + rad) | 0); x++) {
      const dx = x - cx, dy = y - cy
      const u = (dx * cos - dy * sin) / rx
      const v = (dx * sin + dy * cos) / ry
      const d = u * u + v * v
      if (d <= 1) blend(x, y, c[0], c[1], c[2], c[3] ?? 1)
    }
  }
}

/** Qalin chiziq (uchlari yumaloq). */
function capsule(x0, y0, x1, y1, r, c) {
  const minX = Math.max(0, Math.min(x0, x1) - r - 2) | 0
  const maxX = Math.min(W, Math.max(x0, x1) + r + 2) | 0
  const minY = Math.max(0, Math.min(y0, y1) - r - 2) | 0
  const maxY = Math.min(H, Math.max(y0, y1) + r + 2) | 0
  const vx = x1 - x0, vy = y1 - y0
  const len2 = vx * vx + vy * vy || 1

  for (let y = minY; y < maxY; y++) {
    for (let x = minX; x < maxX; x++) {
      let t = ((x - x0) * vx + (y - y0) * vy) / len2
      t = t < 0 ? 0 : t > 1 ? 1 : t
      const px = x0 + vx * t, py = y0 + vy * t
      const d = Math.hypot(x - px, y - py)
      if (d <= r) blend(x, y, c[0], c[1], c[2], c[3] ?? 1)
    }
  }
}

/** Ingichkalashib boruvchi chiziq — oyoq va mo'ylov uchun. */
function taper(pts, r0, r1, c) {
  for (let i = 0; i < pts.length - 1; i++) {
    const a = pts[i], b = pts[i + 1]
    const t0 = i / (pts.length - 1), t1 = (i + 1) / (pts.length - 1)
    const ra = r0 + (r1 - r0) * t0
    const rb = r0 + (r1 - r0) * t1
    const steps = Math.ceil(Math.hypot(b[0] - a[0], b[1] - a[1]) / 2)
    for (let s = 0; s <= steps; s++) {
      const t = s / steps
      const x = a[0] + (b[0] - a[0]) * t
      const y = a[1] + (b[1] - a[1]) * t
      const r = ra + (rb - ra) * t
      ellipse(x, y, r, r, 0, c)
    }
  }
}

// ══════════════════════════════════════════════════════════════════
//  RANGLAR — issiq qora-jigarrang chumoli, sokin och fon.
//  Telegram avatari doira ichida ko'rinadi: fon to'la bo'lishi kerak.
// ══════════════════════════════════════════════════════════════════
const BG      = [246, 243, 238]
const BG_EDGE = [231, 226, 218]
const BODY    = [38, 26, 22]
const BODY_LT = [86, 60, 48]
const SHINE   = [214, 196, 182]
const TEXT    = [26, 22, 20]
const ACCENT  = [150, 62, 34]

// ── Fon: markazdan chetga sekin to'qlashadi ──
{
  const cx = W / 2, cy = H / 2, rmax = Math.hypot(cx, cy)
  for (let y = 0; y < H; y++) {
    for (let x = 0; x < W; x++) {
      const t = Math.min(1, Math.hypot(x - cx, y - cy) / rmax)
      const e = t * t
      blend(x, y,
        BG[0] + (BG_EDGE[0] - BG[0]) * e,
        BG[1] + (BG_EDGE[1] - BG[1]) * e,
        BG[2] + (BG_EDGE[2] - BG[2]) * e, 1)
    }
  }
}

// ══════════════════════════════════════════════════════════════════
//  CHUMOLI — yon ko'rinish, chapga qaragan.
//
//  Haqiqiy chumolining uch belgisi bor va ular aniq ko'rinishi kerak:
//    1. Uch bo'g'in: bosh — ko'krak — qorin, oralari INGICHKA
//    2. Tirsakli mo'ylov — tanadagi eng tanish chiziq
//    3. Oltita oyoq, hammasi KO'KRAKDAN chiqadi, tizzasi tanadan baland
//
//  Shu uchtasi to'g'ri bo'lsa, siluet bir qarashda chumoli deb o'qiladi.
// ══════════════════════════════════════════════════════════════════
const S = W / 1000
const px = v => v * S
const OX = px(60), OY = px(-30)
const P = (x, y) => [px(x) + OX, px(y) + OY]
const E = (x, y, rx, ry, rot, c) => ellipse(px(x) + OX, px(y) + OY, px(rx), px(ry), rot, c)

// ── Yerdagi soya ──
ellipse(px(520) + OX, px(672) + OY, px(300), px(26), 0, [198, 190, 180, 0.42])

// ══ UZOQDAGI OYOQLAR ══
// Ochroq rangda — orqada turgani shundan bilinadi.
const FAR = [78, 54, 44, 0.9]
const leg = (pts, c, r0 = 12, r1 = 3.5) => taper(pts, px(r0), px(r1), c)

// Son yuqoriga ko'tariladi, boldir yerga tushadi — tizza tanadan baland.
leg([P(462, 438), P(388, 338), P(300, 596)], FAR, 10, 3)
leg([P(498, 446), P(536, 322), P(590, 610)], FAR, 10, 3)
leg([P(532, 444), P(658, 340), P(758, 582)], FAR, 10, 3)

// ══ QORIN (gaster) ══
// Cho'zinchoq, biroz egilgan, orqasi ingichkalashadi.
E(712, 432, 168, 132, -0.20, BODY)
E(824, 470, 62, 48, -0.30, BODY)           // orqa uchi

// Bo'g'in chiziqlari — yaltiroq xitin taassuroti.
for (let i = 0; i < 3; i++) {
  const t = 0.18 + i * 0.20
  E(660 + t * 260, 404 + t * 66, 3.2, 96 - i * 14, -0.20, [16, 11, 9, 0.30])
}

// Yuqori qirradagi yorug'lik — yaltiroq sirt shundan sezilaadi.
E(676, 348, 96, 26, -0.30, [SHINE[0], SHINE[1], SHINE[2], 0.30])
E(650, 340, 46, 13, -0.32, [255, 252, 248, 0.42])

// ══ PETIOLA ══
// Chumolining ajratuvchi belgisi: qorin bilan ko'krak orasidagi
// INGICHKA bel va undagi tugun.
E(560, 442, 26, 22, 0, BODY)
E(586, 444, 17, 15, 0, BODY)

// ══ KO'KRAK (mezosoma) ══
E(470, 418, 96, 74, -0.26, BODY)
E(508, 448, 42, 34, -0.20, BODY)           // pastki qism
E(444, 372, 44, 20, -0.44, [SHINE[0], SHINE[1], SHINE[2], 0.26])

// ══ BO'YIN ══ — ingichka, boshni ko'krakdan ajratadi
capsule(px(400) + OX, px(392) + OY, px(360) + OX, px(378) + OY, px(17), BODY)

// ══ BOSH ══
E(300, 362, 92, 80, -0.14, BODY)
E(276, 330, 38, 21, -0.38, [SHINE[0], SHINE[1], SHINE[2], 0.24])

// ── Ko'z ── yon tomonda, biroz oldinga
E(258, 350, 19, 16, -0.1, [10, 7, 6])
E(252, 344, 6.5, 5.5, 0, [255, 250, 245, 0.9])

// ── Jag'lar (mandibula) ── oldinga qaragan, kuchli
taper([P(216, 388), P(166, 404), P(128, 396)], px(12), px(4), BODY)
taper([P(220, 408), P(176, 432), P(142, 436)], px(11), px(4), BODY)

// ══ TIRSAKLI MO'YLOVLAR ══
// Uzun birinchi bo'g'in (skapus), keyin tirsak va ingichka davomi.
taper([P(248, 306), P(206, 236), P(162, 208)], px(9.5), px(5), BODY)
taper([P(162, 208), P(112, 190), P(66, 200)], px(5), px(3), BODY)

taper([P(272, 296), P(250, 220), P(214, 176)], px(9), px(5), BODY)
taper([P(214, 176), P(166, 148), P(120, 150)], px(5), px(3), BODY)

// ══ YAQINDAGI OYOQLAR ══
// Eng oxirida chiziladi — shunda tizza bo'g'ini tana USTIDA ko'rinadi.
// Ilgari ular tanadan oldin chizilgani uchun faqat pastki qismi ko'rinib,
// oyoq tayoqqa o'xshab qolgan edi.
// Oyoq tana ustidan o'tganda bir xil rang qo'shilib ketardi va bo'g'in
// ko'rinmasdi. Shuning uchun avval och chegara, ustidan oyoqning o'zi.
const NEAR = [
  [P(452, 458), P(356, 348), P(252, 646)],
  [P(494, 470), P(522, 326), P(560, 662)],
  [P(528, 464), P(666, 348), P(788, 630)],
]
const HALO = [120, 92, 78, 0.55]   // och jigarrang — kesib yubormaydi
for (const L of NEAR) taper(L, px(16), px(5.5), HALO)
for (const L of NEAR) taper(L, px(13), px(3.5), BODY)

// Bo'g'in tugunlari — tizza aniq ko'rinsin.
for (const L of NEAR) ellipse(L[1][0], L[1][1], px(9), px(9), 0, BODY)

// ══════════════════════════════════════════════════════════════════
//  BIGANT — geometrik, qat'iy harflar. Har harf chiziqlardan quriladi,
//  shu sabab qalinlik va oraliq bir xil chiqadi.
// ══════════════════════════════════════════════════════════════════
function letter(ch, x, y, h, t, c) {
  const w = h * 0.62
  const L = x, R = x + w, T = y, Bo = y + h, M = y + h / 2, C = x + w / 2
  const cap = (a, b, d, e) => capsule(a, b, d, e, t / 2, c)
  const arc = (cx, cy, r, a0, a1) => {
    const n = Math.max(8, Math.ceil((a1 - a0) * r / 6))
    for (let i = 0; i <= n; i++) {
      const a = a0 + (a1 - a0) * i / n
      ellipse(cx + Math.cos(a) * r, cy + Math.sin(a) * r, t / 2, t / 2, 0, c)
    }
  }

  switch (ch) {
    case 'B': {
      cap(L, T, L, Bo)
      const r = h / 4
      cap(L, T, R - r, T); arc(R - r, T + r, r, -Math.PI / 2, Math.PI / 2); cap(R - r, M, L, M)
      cap(L, M, R - r, M); arc(R - r, M + r, r, -Math.PI / 2, Math.PI / 2); cap(R - r, Bo, L, Bo)
      break
    }
    case 'I': cap(C, T, C, Bo); break
    case 'G': {
      // Doira, o'ng tomonida ochiq joy; keyin ichkariga qaragan gorizontal
      // tilcha. Ochiq joy va tilcha bo'lmasa harf «C» bo'lib o'qiladi.
      const r = h / 2
      const a0 = Math.PI * 0.19, a1 = Math.PI * 1.81
      arc(C, M, r, a0, a1)
      const ex = C + Math.cos(a0) * r, ey = M + Math.sin(a0) * r
      cap(ex, ey, ex, M)                 // uchidan yuqoriga qisqa chiziq
      cap(ex, M, C + r * 0.05, M)        // ichkariga tilcha
      break
    }
    case 'A':
      cap(L, Bo, C, T); cap(C, T, R, Bo)
      cap(L + w * 0.17, Bo - h * 0.32, R - w * 0.17, Bo - h * 0.32)
      break
    case 'N': cap(L, Bo, L, T); cap(L, T, R, Bo); cap(R, Bo, R, T); break
    case 'T': cap(L, T, R, T); cap(C, T, C, Bo); break
  }
  return w
}

{
  const word = 'BIGANT'
  const h = px(96)            // harf balandligi
  const t = px(21)            // chiziq qalinligi
  const gap = px(30)

  // Umumiy kenglikni hisoblab, markazga qo'yamiz.
  let total = 0
  for (const ch of word) total += h * 0.62 + gap
  total -= gap

  let x = (W - total) / 2
  const y = px(772)

  for (const ch of word) {
    letter(ch, x, y, h, t, TEXT)
    x += h * 0.62 + gap
  }

  // Nozik chiziq — so'z ostida, urg'u uchun.
  capsule((W - total) / 2, y + h + px(40), (W - total) / 2 + total, y + h + px(40),
          px(4), [ACCENT[0], ACCENT[1], ACCENT[2], 0.9])
}

// ══════════════════════════════════════════════════════════════════
//  Kichraytirish (supersampling) va PNG yozish
// ══════════════════════════════════════════════════════════════════
const out = Buffer.alloc(OUT * OUT * 3)
for (let y = 0; y < OUT; y++) {
  for (let x = 0; x < OUT; x++) {
    let r = 0, g = 0, b = 0
    for (let sy = 0; sy < SS; sy++) {
      for (let sx = 0; sx < SS; sx++) {
        const j = ((y * SS + sy) * W + (x * SS + sx)) * 3
        r += buf[j]; g += buf[j + 1]; b += buf[j + 2]
      }
    }
    const n = SS * SS, i = (y * OUT + x) * 3
    out[i] = Math.round(r / n); out[i + 1] = Math.round(g / n); out[i + 2] = Math.round(b / n)
  }
}

// ── PNG ──
function chunk(type, data) {
  const len = Buffer.alloc(4); len.writeUInt32BE(data.length)
  const t = Buffer.from(type, 'ascii')
  const crcBuf = Buffer.concat([t, data])
  const crc = Buffer.alloc(4); crc.writeUInt32BE(crc32(crcBuf) >>> 0)
  return Buffer.concat([len, t, data, crc])
}
const TBL = (() => {
  const t = new Int32Array(256)
  for (let n = 0; n < 256; n++) {
    let c = n
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xEDB88320 ^ (c >>> 1) : c >>> 1
    t[n] = c
  }
  return t
})()
function crc32(b) {
  let c = -1
  for (let i = 0; i < b.length; i++) c = TBL[(c ^ b[i]) & 0xFF] ^ (c >>> 8)
  return c ^ -1
}

const rawData = Buffer.alloc((OUT * 3 + 1) * OUT)
for (let y = 0; y < OUT; y++) {
  rawData[y * (OUT * 3 + 1)] = 0
  out.copy(rawData, y * (OUT * 3 + 1) + 1, y * OUT * 3, (y + 1) * OUT * 3)
}

const ihdr = Buffer.alloc(13)
ihdr.writeUInt32BE(OUT, 0); ihdr.writeUInt32BE(OUT, 4)
ihdr[8] = 8; ihdr[9] = 2; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0

const png = Buffer.concat([
  Buffer.from([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
  chunk('IHDR', ihdr),
  chunk('IDAT', zlib.deflateSync(rawData, { level: 9 })),
  chunk('IEND', Buffer.alloc(0)),
])

const dest = process.argv[2] || 'bigant-bot.png'
fs.writeFileSync(dest, png)
console.log('yozildi:', dest, '|', OUT + 'x' + OUT, '|', (png.length / 1024).toFixed(0) + ' KB')
