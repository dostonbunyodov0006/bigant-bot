# Bigant — hujjat aylanishi va qarzdorlik nazorati

«BIGANT» MChJ (Toshkent, Dombirobod — santexnika va elektr mollari ulgurji savdosi)
uchun **ekransiz xizmat**: Didox'dan hujjatlarni o'qiydi, zanjirini eslab qoladi va
Telegram bot orqali bugalterga xabar beradi.

**Veb-interfeys yo'q va ataylab qo'shilmagan.** Bugalter hujjatni Didox'ning o'zida
yaratadi va yuboradi; bu tizim faqat kuzatadi, bog'laydi va ogohlantiradi.

---

## Nima uchun bu tizim kerak

Didox — hujjat pochtasi. U quyidagilarni **bilmaydi**:

| Savol | Didox | Bu tizim |
|---|---|---|
| Falon MChJ dan faktura keldimi? | ✅ | ✅ |
| Faktura **aynan qaysi doverennost** uchun? | ⚠️ faqat yuboruvchi `FacturaEmpowermentDoc` ni to'ldirsa | ✅ doim |
| Doverennost muddati tugayaptimi, tovar hali kelmadimi? | ❌ | ✅ |
| Falon MChJ ga qancha pul o'tkazdik, qancha qarzimiz qoldi? | ❌ (Didox bank emas) | ✅ |
| Doverennostsiz faktura kelib qoldimi? | ❌ | ✅ ogohlantiradi |

---

## Ish oqimi

### XARID (postavshikdan olamiz)
```
1. Postavshik DOGOVOR yuboradi        →  🔔 «Uzpolimer dan Dogovor keldi»
2. Biz DOVERENNOST yuboramiz          →  (Didox'da yaratiladi)
3. Postavshik TTN yuboradi            →  🔔 «...Doverennost: №DOV-2026-00004»
4. Postavshik FAKTURA yuboradi        →  🔔 «...Doverennost: №DOV-2026-00004»
5. Bugalter to'lovni botga yozadi     →  🔔 «Qoldi to'lash: 25 000 000»
```

### SOTUV (klientga sotamiz) — teskarisi
```
1. Biz DOGOVOR + SCHYOT yuboramiz
2. Klient DOVERENNOST yuboradi        →  🔔 xabar
3. Biz TTN + FAKTURA yuboramiz
4. Klient pul o'tkazadi               →  bugalter botga yozadi
```

Har bir hujjat zanjirdagi oldingi bo'g'inga bog'lanadi (`documents.parent_document_id`).
Bog'lanmasa — **qizil ogohlantirish**.

---

## Telegram bot

Ulash: botni oching, bir marta `/start` yozing. Boshqa hech narsa kerak emas —
kod, ro'yxatdan o'tish yo'q. `/start` yozilgan chat xabar manziliga aylanadi.

### Tugmalar
| Tugma | Nima ko'rsatadi |
|---|---|
| 🔴 **Qarzimiz** | biz qaysi MChJ ga qancha o'tkazganmiz, qancha qolgan |
| 🟢 **Qarzdorlar** | qaysi MChJ bizga qancha o'tkazgan, qancha qolgan |
| 🔗 **Zanjir** | dogovor → doverennost → TTN → faktura tekshiruvi |
| 📊 **Holat** | ochiq shartnomalar, shoshilinch holatlar |

### To'lov kiritish

Didox pul harakatini ko'rmaydi — **ikkala yo'nalishni ham bugalter yozadi**
(klient bizning botimizga kirmaydi):

```
5 mln tolandi Uzpolimerga            →  BIZ to'ladik
10 mln otkazdik Temir Yolga          →  BIZ to'ladik
5 mln Uniform MChJ bizga otkazdi     →  BIZGA tushdi
12 mln Baraka Stroy bizga tushdi     →  BIZGA tushdi
```

Bot darhol javob beradi: qancha o'tkazilgan, qancha qolgan.
Yo'nalish matndagi «bizga / tushdi / keldi» so'zlaridan aniqlanadi.

Muqobil: `/api/payments/import` — bank vipiskasini ko'p qatorli import
(hozir bot orqali emas, HTTP orqali).

### Buyruqlar
`/holat` `/qarzimiz` `/qarzdorlar` `/qarz` `/zanjir [raqam]` `/tolov` `/start` `/ochirish` `/yordam`

---

## Avtomatik ogohlantirishlar

Har 30 daqiqada tekshiriladi (`ReminderJob`):

| Ogohlantirish | Qachon |
|---|---|
| 🔴 Pul keldi, tovar berilmadi | to'lov bor, TTN yo'q |
| 🔴 **Zanjir uzilgan** | faktura/TTN bor, doverennost yo'q |
| ⏰ Doverennost muddati | tugashiga 3 kun qolganda; o'tsa Critical |
| ⏰ Qadam kechikdi | cheklist muddati o'tgan |
| 💰 To'lov | qisman yoki to'liq — qolgan qarz bilan |
| 📊 Kunlik xulosa | har kuni 09:00 (Toshkent) |

Tunda (22:00–08:00) faqat Critical xabarlar boradi.

---

## Arxitektura

```
src/
  Bigant.Domain/          entitylar, enumlar, DealWorkflow (barcha biznes qoidalar)
  Bigant.Didox/           IDidoxClient + Null/Http realizatsiyalari, JSON sxemalar
  Bigant.Infrastructure/  EF Core, servislar, Hangfire jobslar, Telegram
  Bigant.Api/             Minimal API endpointlar + Program.cs
```

**Muhim:** `DealWorkflow.cs` — biznes qoidalarining yagona manbai.
Cheklist qadamlari, hujjat zanjiri, kim qaysi hujjatni chiqarishi — hammasi shu yerda.

### Texnologiyalar
.NET 10 · PostgreSQL 18 · EF Core · Hangfire (PostgreSQL storage) · Serilog

### Fon vazifalari
| Job | Jadval | Vazifasi |
|---|---|---|
| `InboxJob` | har 10 daq | Didox'dan kiruvchi hujjatlarni oladi |
| `ReminderJob` | har 30 daq | ogohlantirishlar |
| `DigestJob` | 09:00 | kunlik xulosa |
| `OutboxJob` | har 1 daq | chiquvchi hujjat navbati (Didox yoqilganda) |
| `StatusSyncJob` | har 15 daq | yuborilgan hujjat holati |

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
```bash
dotnet build
cd src/Bigant.Api
ASPNETCORE_ENVIRONMENT=Development dotnet run --urls http://localhost:5080
```

Migratsiyalar avtomatik qo'llanadi (`DbSeeder.SeedAsync`).
Tekshirish: `curl http://localhost:5080/health` → `{"status":"ok","didox":false}`

---

## DIDOX INTEGRATSIYASI

Tizim Didox'siz **to'liq ishlaydi** (`NullDidoxClient`). Yoqish uchun:

### 1. Sozlama
```jsonc
"Didox": {
  "Enabled": true,                     // ← DI shu yerdan DidoxHttpClient ga o'tadi
  "BaseUrl": "https://api.didox.uz",   // test uchun: https://testapi3.didox.uz
  "CompanyInn": "301234567"
}
```
```bash
dotnet user-secrets set "Didox:PartnerToken" "..."
dotnet user-secrets set "Didox:Login" "..."
dotnet user-secrets set "Didox:Password" "..."
```

### 2. `src/Bigant.Didox/DidoxHttpClient.cs` — 3 ta joy to'ldirilishi kerak

Kodda `TODO(didox)` deb belgilangan. Manzillar **taxminiy** — rasmiy developer
hujjatidan tasdiqlang:

| Qator | Nima | Hozirgi taxmin |
|---|---|---|
| ~56 | auth endpoint | `v1/auth/login` |
| ~88 | hujjat yuborish | `v1/documents/{tur}` |
| ~127 | **kiruvchi hujjatlar** | `v1/documents/inbox?from=...` |

Uchinchisi eng muhim — butun notification oqimi shundan boshlanadi.

`PullInboxAsync` → `DidoxInboxItem` qaytaradi → `InboxJob` uni
`IInboxService.RegisterAsync` ga uzatadi. **Qolgan hamma kod o'zgarmaydi** —
zanjir, cheklist, xabar, qarz — hammasi shu bitta nuqtadan oziqlanadi.

### 3. E-IMZO
Didox yoqilsa imzo majburiy bo'ladi (`MarkSignedAsync` da tekshiriladi).
Kalit **hech qachon serverda saqlanmaydi** — imzo brauzerda, lokal WebSocket orqali
qo'yiladi. Bugalterning kompyuteriga E-IMZO o'rnatiladi.

### 4. Didox'dan oldin bajarilishi shart
```bash
curl http://localhost:5080/api/didox-readiness -H "Authorization: Bearer <token>"
```
- **MXIK kodlari** — hozir 38 guruhdan 36 tasida yo'q. MXIK'siz soliq fakturani
  qabul qilmaydi. MXIK guruhdan meros olinadi (`Product.EffectiveMxik`), shuning
  uchun 2000+ tovarga emas, 38 guruhga bir martadan kiritish yetadi.
  Kodlar: [tasnif.soliq.uz](https://tasnif.soliq.uz)
- Kompaniya rekvizitlari to'liq (direktor PINFL, h/r, MFO, OKED)
- Kontragentlarda direktor F.I.Sh — Didox ikkala tomon uchun majburiy qiladi

---

## SERVERGA DEPLOY

### Environment variable'lar
`dotnet user-secrets` **faqat Development'da** ishlaydi. Production'da env var:

```bash
ConnectionStrings__Db="Host=...;Database=bigant;Username=...;Password=..."
Jwt__Key="<32+ belgi>"
Telegram__BotToken="..."
Didox__Enabled=true
Didox__PartnerToken="..."
Didox__Login="..."
Didox__Password="..."
Didox__BaseUrl="https://api.didox.uz"
Didox__CompanyInn="301234567"
BIGANT_ADMIN_PASSWORD="..."     # faqat birinchi marta
ASPNETCORE_ENVIRONMENT=Production
```

### Tekshiruv ro'yxati
- [ ] PostgreSQL 18, `bigant` bazasi, kunlik backup
- [ ] `Telegram:WebAppUrl` — bo'sh qoldiring (veb-interfeys yo'q)
- [ ] Hangfire paneli faqat Development'da ochiq (`Program.cs` da tekshirilgan) —
      Production'da ochmoqchi bo'lsangiz auth filtri qo'ying
- [ ] `logs/` katalogi yozilishi mumkin bo'lsin; 30 kun saqlanadi
- [ ] Xizmat qayta ishga tushganda avtomatik ko'tarilsin (systemd / Windows Service)
- [ ] Server vaqti muhim emas — kod Toshkent vaqtini `UzTime` orqali o'zi hisoblaydi,
      bazada UTC saqlanadi

### Xavfsizlik
- Telegram tokeni URL ichida ketadi, shuning uchun `System.Net.Http.HttpClient`
  loggeri `Warning` ga tushirilgan — aks holda token log fayliga yozilardi
- `.gitignore`: `logs/`, `appsettings.Production.json`, `*.pfx`, `.env`
- Login endpointida rate limit (daqiqasiga 10 urinish)

---

## HTTP API

Veb-interfeys yo'q, lekin endpointlar **test va integratsiya uchun** ishlaydi
(hammasi JWT talab qiladi, `/health` dan tashqari).

| Guruh | Asosiy endpointlar |
|---|---|
| `/api/auth` | `login`, `me`, `users` |
| `/api/inbox` | `POST` — kiruvchi hujjatni qo'lda kiritish (Didox bilan **bir xil yo'l**) |
| `/api/deals` | ro'yxat, kartochka, cheklist qadamlari |
| `/api/documents` | yaratish, imzolash, navbat |
| `/api/payments` | to'lov, `debts`, `import` |
| `/api/notifications` | ro'yxat, `run-check` |
| `/api/didox-readiness` | Didox'ga tayyorlik tekshiruvi |

> `POST /api/inbox` — `InboxService.RegisterAsync` ni chaqiradi, ya'ni Didox'ning
> `InboxJob` bilan **aynan bitta kod yo'li**. Didox'siz test qilish uchun ishlatiladi.

---

## Ma'lum cheklovlar

1. **Pul ma'lumoti Didox'dan kelmaydi.** Shartnoma summasi Didox'dan, to'langan
   summa faqat bugalter kiritganidan. Bugalter to'lovni yozishni unutsa, qarz
   haqiqiydan katta ko'rinadi. Ishonchliroq yo'l — bank vipiskasi importi.
2. **`FacturaEmpowermentDoc` chiquvchi fakturamizda `null`.** Bizning bazamizda
   zanjir bor, lekin Didox'da ko'rinmaydi. Rasmiy sxema kelganda to'ldiriladi.
3. **`DidoxHttpClient` — skelet.** Yuqoridagi 3 ta `TODO(didox)` to'ldirilmaguncha
   Didox bilan real aloqa yo'q.

---

## Tarix

Loyiha dastlab veb-dashboard sifatida qurilgan edi. Egasining qarori bilan
(2026-08-18) interfeys butunlay olib tashlandi: hujjat ishlari Didox'da bajariladi,
bu tizim faqat kuzatuv va xabar berish uchun qoladi. Sotuvchi roli ham olib
tashlandi — tizimdan faqat bugalter foydalanadi.
