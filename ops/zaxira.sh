#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════
#  BIGANT — BAZANING KUNLIK ZAXIRASI
#
#  Nega kerak: bu bazada butun buxgalteriya yotadi — shartnomalar,
#  hujjat zanjirlari, qarzlar. Disk buzilsa yoki noto'g'ri buyruq
#  berilsa, zaxirasiz hech narsani qaytarib bo'lmaydi.
#
#  Ishlatish:
#      ./ops/zaxira.sh
#
#  Sozlamalar (environment variable orqali):
#      PGHOST, PGPORT, PGUSER, PGPASSWORD, PGDATABASE  — Postgres
#      BIGANT_BACKUP_DIR   — zaxira qayerga yoziladi (default: ~/bigant-zaxira)
#      BIGANT_KEEP_DAYS    — necha kunlik saqlanadi (default: 30)
#      BIGANT_TG_TOKEN     — xato bo'lsa Telegramga xabar (ixtiyoriy)
#      BIGANT_TG_CHAT      — qaysi chatga
#
#  MUHIM: zaxira JIM QOLIB KETMASIN. Eng ko'p uchraydigan xato —
#  zaxira oylab ishlamay turadi, buni esa faqat kerak bo'lganda
#  bilib qolishadi. Shuning uchun skript:
#    • faylni tekshiradi (bo'sh yoki chala bo'lsa xato beradi)
#    • xato bo'lsa Telegramga xabar yuboradi
#    • xato bo'lsa nol bo'lmagan kod bilan chiqadi
# ═══════════════════════════════════════════════════════════════════
set -uo pipefail

QAYERGA="${BIGANT_BACKUP_DIR:-$HOME/bigant-zaxira}"
SAQLASH_KUNI="${BIGANT_KEEP_DAYS:-30}"
BAZA="${PGDATABASE:-bigant}"
SANA="$(date +%Y-%m-%d_%H%M)"
FAYL="$QAYERGA/bigant-$SANA.sql.gz"
LOG="$QAYERGA/zaxira.log"

mkdir -p "$QAYERGA"

yoz() { echo "$(date '+%Y-%m-%d %H:%M:%S')  $*" | tee -a "$LOG"; }

xabar_ber() {
  # Telegram sozlanmagan bo'lsa jim o'tamiz — skriptning asosiy ishi
  # zaxira olish, xabar yuborish emas.
  [ -n "${BIGANT_TG_TOKEN:-}" ] && [ -n "${BIGANT_TG_CHAT:-}" ] || return 0
  curl -s -m 20 -o /dev/null \
    -X POST "https://api.telegram.org/bot${BIGANT_TG_TOKEN}/sendMessage" \
    --data-urlencode "chat_id=${BIGANT_TG_CHAT}" \
    --data-urlencode "text=$1" || true
}

tugat_xato() {
  # Tekshiruvdan o'tmagan fayl «zaxira» bo'lib turmasin: nomini
  # o'zgartiramiz, shunda uni ko'rgan odam ham, keyingi tozalash ham
  # uni haqiqiy nusxa deb o'ylamaydi.
  [ -f "$FAYL" ] && mv "$FAYL" "$FAYL.BUZUQ" 2>/dev/null
  yoz "XATO: $1"
  xabar_ber "🔴 BIGANT ZAXIRASI OLINMADI

$1

Server: $(hostname)
Vaqt: $(date '+%d.%m.%Y %H:%M')

⚠️ Baza himoyasiz qolmoqda — tekshiring."
  exit 1
}

yoz "─── zaxira boshlandi: $BAZA → $FAYL"

# ── 1. Nusxa olish ──
if ! pg_dump --no-owner --no-privileges "$BAZA" 2>>"$LOG" | gzip -9 > "$FAYL"; then
  rm -f "$FAYL"
  tugat_xato "pg_dump ishlamadi. Postgres o'chiq yoki parol noto'g'ri bo'lishi mumkin."
fi

# ── 2. Fayl haqiqatan to'lganini tekshirish ──
# Bo'sh yoki juda kichik fayl — bu zaxira emas, yolg'on xotirjamlik.
HAJM=$(wc -c < "$FAYL" | tr -d ' ')
if [ "$HAJM" -lt 10000 ]; then
  tugat_xato "Zaxira fayli juda kichik ($HAJM bayt) — ma'lumot tushmagan."
fi

# ── 3. Arxiv buzilmaganini va ichida jadval borligini tekshirish ──
if ! gzip -t "$FAYL" 2>>"$LOG"; then
  tugat_xato "Zaxira arxivi buzuq (gzip -t o'tmadi)."
fi

# DIQQAT: bu yerda «grep -q» ISHLATILMAYDI. U birinchi mos qatorda
# to'xtaydi, gzip esa SIGPIPE oladi va «set -o pipefail» tufayli butun
# quvur xato deb hisoblanadi — natijada TO'G'RI zaxira ham «chala» deb
# rad etilardi (sinovda aynan shu bo'ldi). «grep -c» oqimni oxirigacha
# o'qiydi, shuning uchun bunday bo'lmaydi.
if [ "$(gzip -dc "$FAYL" | grep -c 'CREATE TABLE public.deals')" -eq 0 ]; then
  tugat_xato "Zaxira ichida «deals» jadvali yo'q — nusxa chala."
fi

yoz "zaxira tayyor: $FAYL ($((HAJM / 1024)) KB)"

# ── 4. Eskilarini tozalash ──
ESKI=$(find "$QAYERGA" -name 'bigant-*.sql.gz' -mtime +"$SAQLASH_KUNI" | wc -l | tr -d ' ')
if [ "$ESKI" -gt 0 ]; then
  find "$QAYERGA" -name 'bigant-*.sql.gz' -mtime +"$SAQLASH_KUNI" -delete
  yoz "$ESKI ta eski zaxira o'chirildi (${SAQLASH_KUNI} kundan eski)"
fi

JAMI=$(find "$QAYERGA" -name 'bigant-*.sql.gz' | wc -l | tr -d ' ')
yoz "─── tugadi. Saqlanayotgan zaxira: $JAMI ta"
exit 0
