# Bigant — hujjat aylanishi va qarzdorlik nazorati

«BIGANT» MChJ (Toshkent, Dombirobod — santexnika va elektr mollari ulgurji savdosi) uchun.

**Hujjat ishi Didox'da bajariladi.** Bugalter dogovor, doverennost, TTN va
fakturani Didox'ning o'zida yaratadi va yuboradi. Bu tizim ularni **kuzatadi,
bir-biriga bog'laydi, ogohlantiradi va hisobot beradi** — Telegram bot va
rolli dashboard orqali.

---

## Nima uchun kerak

Didox — hujjat pochtasi. U quyidagilarni **bilmaydi**:

| Savol | Didox | Bu tizim |
|---|---|---|
| Falon MChJ dan faktura keldimi? | ✅ | ✅ |
| Faktura **aynan qaysi doverennost** uchun? | ⚠️ faqat yuboruvchi `FacturaEmpowermentDoc` ni to'ldirsa | ✅ doim |
| Doverennost muddati tugayaptimi, tovar hali kelmadimi? | ❌ | ✅ |
| Falon MChJ ga qancha o'tkazdik, qancha qarzimiz qoldi? | ❌ Didox bank emas | ✅ |
| Doverennostsiz faktura kelib qoldimi? | ❌ | ✅ ogohlantiradi |
| 30 kunda nima bo'ldi? | ❌ | ✅ hisobot |

---

## Ish oqimi

### XARID — postavshikdan olamiz
```
1. Postavshik DOGOVOR yuboradi     → 📄 «Dogovor keldi»
2. Biz DOVERENNOST yuboramiz       → 📤 «Doverennost yuborildi» + muddati
3. Postavshik TTN yuboradi         → 🚚 «...Doverennost №DOV-... uchun»
4. Postavshik FAKTURA yuboradi     → 🧾 «...Doverennost №DOV-... uchun»
5. Bugalter to'lovni botga yozadi  → 💰 «Qoldi to'lash: 25 000 000»
```

### SOTUV — klientga sotamiz (teskarisi)
```
1. Biz DOGOVOR + SCHYOT yuboramiz  → 📤 xabar
2. Klient DOVERENNOST yuboradi     → 📝 xabar
3. Biz TTN + FAKTURA yuboramiz     → 📤 xabar
4. Klient pul o'tkazadi            → bugalter botga yozadi
```

Har hujjat zanjirdagi oldingi bo'g'inga bog'lanadi (`documents.parent_document_id`).
Bog'lanish ikki manbadan: avval **Didox'ning o'z havolasi**
(`FacturaEmpowermentDoc.EmpowermentNo`), topilmasa tartib bo'yicha taxmin.
Bog'lanmasa — **qizil ogohlantirish**.

---

## Rollar

Har rol **faqat o'z ishini** ko'radi — backend ham, dashboard ham tekshiradi.

| Rol | Savoli | Ko'radi |
|---|---|---|
| **Realizatsiya** | Nima sotdik, kimga, qancha, puli keldimi? | sotuv bitimlari, INN nazorati, QQS, debitor qarz |
| **Perehod** | Qaysi hujjat qaysiga tegishli, zanjir butunmi? | hujjat zanjiri, uzilishlar, kutilayotgan hujjatlar |
| **Admin** | Hammasi | ikkala yo'nalish, foydalanuvchilar, sozlamalar |

Har rolning o'z tusi bor: Realizatsiya po'lat-ko'k, Perehod yashil-moviy, Admin shifer.

**Hisobot** — hamma rolga, o'z kesimi bilan: 30 / 90 / 180 / 365 kun.
Bitimlar, pul harakati, hujjat harakati turlar bo'yicha, kontragent kesimi, zanjir sog'ligi.

---

## Telegram bot

Ulash: botni oching, bir marta `/start` yozing. **Birinchi `/start` kanalni egallaydi** —
shundan keyin faqat o'sha chat va ro'yxatdagi xodimlar javob oladi, begonaga
«kirish huquqi yo'q» deb javob qaytadi.

### Tugmalar
🔴 **Qarzimiz** · 🟢 **Qarzdorlar** · 🔗 **Zanjir** · 📊 **Holat**

### To'lov kiritish

Didox pul harakatini ko'rmaydi — **ikkala yo'nalishni ham bugalter yozadi**
(klient bizning botimizga kirmaydi). Istalgan yozilish tanib olinadi:

```
5 mln uniform                     →  BIZ to'ladik
uniform mchj 5 mln                →  BIZ to'ladik
5 mln tolandi Uzpolimerga         →  BIZ to'ladik
5 mln Uniform MChJ bizga otkazdi  →  BIZGA tushdi
12 mln Baraka Stroy bizga tushdi  →  BIZGA tushdi
```

Yo'nalish matndagi «bizga / tushdi / keldi» so'zlaridan aniqlanadi.
Bot darhol javob beradi va **o'sha zahoti** dashboardga ham xabar chiqadi.

Muqobil: `POST /api/payments/import` — bank vipiskasini ko'p qatorli import.

---

## Xabarlar — 19 tur

Har xabar **alohida** chiqadi, ish bo'lgan zahoti. Jamlanmaydi.

### Realizatsiya (8)
🔴 Pul keldi tovar berilmadi · 💸 To'lov kelmadi · 💰 To'lov o'tkazildi/tushdi ·
✅ Qarz yopildi · 📆 Qarz 30/60/90 kundan oshdi · 💰 Ortiqcha to'lov ·
📋 Kontragent ma'lumoti to'liq emas · ✅ Shartnoma yopildi

### Perehod (9)
📄🚚🧾 Hujjat keldi · 📤 Hujjat yuborildi · ⏳ Muddati yaqin (3 kun) ·
⏰ Kechikdi · ⏳ Doverennost muddati · 🚨 Zanjir uzilgan · ⚠️ Summa mos kelmadi ·
✅ Zanjir to'liq · 🕛 Bitim uzoq ochiq · 📎 Hujjat bog'lanmagan

### Admin (2)
🔴 Didox aloqasi uzildi (3 marta ketma-ket xato) · ✅ Aloqa tiklandi

### Hammaga (1)
📊 Kunlik xulosa — 09:00 (Toshkent)

Tunda (22:00–08:00) faqat Critical xabarlar boradi.
Uzun xabar avtomatik bo'linadi (Telegram chegarasi 4096).

---

## Arxitektura

```
src/
  Bigant.Domain/          entitylar, enumlar, DealWorkflow (barcha biznes qoidalar)
  Bigant.Didox/           IDidoxClient + Null/Http realizatsiyalari
  Bigant.Infrastructure/  EF Core, servislar, Hangfire jobslar, Telegram
  Bigant.Api/             Minimal API + wwwroot (dashboard shu yerdan tarqatiladi)
web/                      React dashboard (build → src/Bigant.Api/wwwroot)
```

**`DealWorkflow.cs` — biznes qoidalarining yagona manbai.** Cheklist qadamlari,
hujjat zanjiri, kim qaysi hujjatni chiqarishi, kim harakat qilishi kerakligi.

.NET 10 · PostgreSQL 18 · EF Core · Hangfire · Serilog · React 19 + Vite

### Fon vazifalari
| Job | Jadval | Vazifasi |
|---|---|---|
| `InboxJob` | `Didox:InboxPollMinutes` | Didox'dan **ikkala yo'nalish** hujjatlari |
| `ReminderJob` | har 30 daq | 12 xil ogohlantirish |
| `DigestJob` | 09:00 | kunlik xulosa |

---

## Ishga tushirish

### 1. Baza
```bash
createdb bigant
```

### 2. Sirlar (kodga YOZILMAYDI)
```bash
cd src/Bigant.Api
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Db" "Host=localhost;Port=5432;Database=bigant;Username=postgres;Password=..."
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)"
dotnet user-secrets set "Telegram:BotToken" "BotFather bergan token"
export BIGANT_ADMIN_PASSWORD='...'      # faqat birinchi ishga tushirishda
```

### 3. Ishga tushirish

**Ishlab chiqish** — front alohida, jonli qayta yuklash bilan:
```bash
dotnet build
cd src/Bigant.Api && ASPNETCORE_ENVIRONMENT=Development dotnet run --urls http://localhost:5080
# boshqa terminalda:
cd web && npm install && npm run dev     # http://localhost:5177 (API ga proksi)
```

**Bitta xizmat** — serverdagi kabi:
```bash
cd web && npm run build          # natija: src/Bigant.Api/wwwroot
cd ../src/Bigant.Api && dotnet run --urls http://localhost:5080
# dashboard ham, API ham bitta manzilda
```

> **CORS kerak emas.** Dashboard backendning o'zidan tarqatiladi (bir xil manba),
> ishlab chiqishda vite proksi ishlatiladi. `Cors:Origins` bo'sh — eng xavfsiz holat.

Migratsiyalar avtomatik qo'llanadi. Tekshirish:
`curl http://localhost:5080/health` → `{"status":"ok","didox":false}`

---

## DIDOX INTEGRATSIYASI

Tizim Didox'siz **to'liq ishlaydi** (`NullDidoxClient`). Yoqish uchun:

### 1. Sozlama
```jsonc
"Didox": {
  "Enabled": true,                     // ← DI shu yerdan DidoxHttpClient ga o'tadi
  "BaseUrl": "https://api.didox.uz",   // test: https://testapi3.didox.uz
  "CompanyInn": "301234567",
  "InboxPollMinutes": 2                // qanchalik tez xabar kerak bo'lsa
}
```
```bash
dotnet user-secrets set "Didox:PartnerToken" "..."
dotnet user-secrets set "Didox:Login" "..."
dotnet user-secrets set "Didox:Password" "..."
```

### 2. `src/Bigant.Didox/DidoxHttpClient.cs` — 3 ta joy to'ldiriladi

Kodda `TODO(didox)` deb belgilangan. Manzillar **taxminiy** — rasmiy developer
hujjatidan tasdiqlang:

| Nima | Hozirgi taxmin |
|---|---|
| auth endpoint | `v1/auth/login` |
| **kiruvchi hujjatlar** | `v1/documents/inbox?from=...` |
| **chiquvchi hujjatlar** | `v1/documents/outbox?from=...` |

Oxirgi ikkitasi eng muhim — butun xabar oqimi shulardan boshlanadi.
Chiquvchisisiz «dogovor tashlandimi?» degan savolga javob bo'lmaydi.

`PullInboxAsync` / `PullOutboxAsync` → `DidoxInboxItem` → `InboxJob` uni
`IInboxService.RegisterAsync` ga uzatadi. **Qolgan hamma kod o'zgarmaydi** —
zanjir, cheklist, xabar, qarz — hammasi shu bitta nuqtadan oziqlanadi.

### 3. Ixtiyoriy, lekin foydali
`InboxService.ExtractEmpowermentNumber` va `ReadEmpowerment` — Didox xom JSON idan
doverennost raqami va **amal qilish muddati** o'qiladi. Maydon nomlari `TODO(didox)`
bilan belgilangan. Noto'g'ri nom hech narsani buzmaydi: kod jimgina taxminga qaytadi,
lekin muddat ogohlantirishi aniqroq bo'lishi uchun tasdiqlashga arziydi.

### 4. Didox'dan oldin
```bash
curl http://localhost:5080/api/didox-readiness -H "Authorization: Bearer <token>"
```
- Kompaniya rekvizitlari to'liq (direktor PINFL, h/r, MFO, OKED)
- Kontragentlarda direktor F.I.Sh — Didox ikkala tomon uchun majburiy qiladi

> **MXIK kerak emas.** Tovar katalogi olib tashlangan: hujjatni Didox yaratadi,
> tovar ro'yxatini bugalter Didox'ga kiritadi.

---

## SERVERGA DEPLOY (Mac mini)

Loyiha macOS'da to'liq ishlaydi: Windows'ga xos API yo'q, fayl yo'llari
`Path.Combine` bilan, vaqt mintaqasi `UzTime.Zone` orqali
(`Asia/Tashkent` → topilmasa qat'iy UTC+5).

```bash
brew install --cask dotnet-sdk
brew install postgresql@18 && brew services start postgresql@18
createdb bigant

git clone <repo> ~/bigant && cd ~/bigant
cd web && npm install && npm run build && cd ..
dotnet publish src/Bigant.Api -c Release -o ~/bigant/app
```

### Environment variable'lar
`dotnet user-secrets` **faqat Development'da** ishlaydi. Production'da:

```bash
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__Db="Host=localhost;Database=bigant;Username=...;Password=..."
Jwt__Key="<32+ belgi, BARQAROR>"
Telegram__BotToken="..."
Didox__Enabled=true
Didox__PartnerToken="..."
Didox__Login="..."
Didox__Password="..."
Didox__BaseUrl="https://api.didox.uz"
Didox__CompanyInn="301234567"
BIGANT_ADMIN_PASSWORD="..."     # faqat birinchi marta
```

`~/Library/LaunchAgents/uz.bigant.bot.plist` — kirishda o'zi ishga tushadi,
qulasa `KeepAlive` qayta ko'taradi:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>Label</key><string>uz.bigant.bot</string>
  <key>ProgramArguments</key>
  <array>
    <string>/Users/USERNAME/bigant/app/Bigant.Api</string>
    <string>--urls</string><string>http://127.0.0.1:5080</string>
  </array>
  <key>WorkingDirectory</key><string>/Users/USERNAME/bigant/app</string>
  <key>EnvironmentVariables</key>
  <dict>
    <key>ASPNETCORE_ENVIRONMENT</key><string>Production</string>
    <key>ConnectionStrings__Db</key><string>...</string>
    <key>Jwt__Key</key><string>...</string>
    <key>Telegram__BotToken</key><string>...</string>
  </dict>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><true/>
  <key>StandardOutPath</key><string>/Users/USERNAME/bigant/out.log</string>
  <key>StandardErrorPath</key><string>/Users/USERNAME/bigant/err.log</string>
</dict></plist>
```

```bash
launchctl load ~/Library/LaunchAgents/uz.bigant.bot.plist
curl http://127.0.0.1:5080/health
```

### Agar oldiga nginx/Caddy qo'yilsa (HTTPS uchun)

Xizmat to'g'ridan-to'g'ri `127.0.0.1` da tursa hech narsa sozlash shart emas.
Lekin oldiga proksi qo'yilsa — masalan HTTPS kerak bo'lganda — **albatta**
quyidagini yoqing:

```bash
Hosting__BehindProxy=true
Hosting__KnownProxies=127.0.0.1        # bo'sh qoldirilsa har qanday proksiga ishonadi
```

Sababi: proksi ortida har bir so'rov proksining manzilidan kelgandek
ko'rinadi. IP bo'yicha chegara (kirish — daqiqasiga 10 ta) o'shanda
**butun kompaniyaga umumiy** bo'lib qoladi: bitta odam parolni bir necha
marta xato kiritsa, qolganlar bir daqiqa kira olmaydi.

Nginx uchun yana ikki narsa — `/api/live` uzluksiz oqim (SSE):

```nginx
location /api/live {
    proxy_pass http://127.0.0.1:5080;
    proxy_buffering off;         # bo'lmasa xabar real vaqtda yetib bormaydi
    proxy_read_timeout 3600s;    # bo'lmasa ulanish uzilib turadi
}
```

**Bitta nusxa ishlasin.** Telegram `getUpdates` bitta vaqtda faqat bitta
mijozga ruxsat beradi: ikkita nusxa ko'tarilsa bot xabarlarni navbat bilan
o'g'irlaydi va `/start` yo'qolib qoladi.

### Tekshiruv ro'yxati
- [ ] `web` qurilgan: `npm run build` → `src/Bigant.Api/wwwroot`
- [ ] `Jwt__Key` **barqaror** — har qayta ishga tushishda o'zgarsa hamma
      foydalanuvchi tizimdan chiqib ketadi
- [ ] `Cors:Origins` bo'sh — dashboard bir xil manbadan
- [ ] PostgreSQL kunlik backup
- [ ] `sudo pmset -a sleep 0 disksleep 0` — Mac mini uxlamasin
- [ ] Tashqi port ochish **shart emas**: xizmat faqat chiquvchi so'rov qiladi
      (Telegram long polling + Didox). `127.0.0.1` ga bog'lash eng xavfsizi.

### Xavfsizlik
- Telegram tokeni URL ichida ketadi → `System.Net.Http.HttpClient` loggeri
  `Warning` ga tushirilgan, aks holda token log fayliga yozilardi
- Botga faqat egallangan chat va ro'yxatdagi xodimlar kira oladi
- Barcha API endpointlari `RequireAuthorization` + rate limit (300/daq)
- Login: BCrypt, minimum 8 belgi, rate limit 10/daq
- Hangfire paneli faqat Development'da
- Xato javobida stack trace chiqmaydi
- `.gitignore`: `logs/`, `wwwroot/`, `appsettings.Production.json`, `*.pfx`

---

## Ma'lum cheklovlar

1. **Pul ma'lumoti Didox'dan kelmaydi.** Shartnoma summasi Didox'dan, to'langan
   summa faqat bugalter kiritganidan. Bugalter yozishni unutsa qarz haqiqiydan
   katta ko'rinadi. Ishonchliroq yo'l — bank vipiskasi importi.
2. **Zanjir ikki manbadan.** Didox havolasi bo'lsa aniq, bo'lmasa tartib bo'yicha
   taxmin. Bitta kontragent bilan bir vaqtda ikkita yetkazib berish ketsa
   adashishi mumkin.
3. **`DidoxHttpClient` — skelet.** 3 ta `TODO(didox)` to'ldirilmaguncha real aloqa yo'q.

---

## Tarix

Loyiha veb-dashboard sifatida boshlangan, keyin egasining qarori bilan
faqat xizmat + botga qisqartirilgan, so'ng rolli **kuzatuv** dashboardi qaytarilgan.
Hujjat **chiqarish** mashinasi va tovar katalogi olib tashlangan: hujjat Didox'da
yaratiladi, bu tizim kuzatadi va ogohlantiradi.
