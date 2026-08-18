# Maxfiy ma'lumotlar — hech qachon kodga yozilmaydi

Barcha sirlar `dotnet user-secrets` yoki environment variable orqali beriladi.
`appsettings.json` da ular **bo'sh** turadi va git'ga tushadi.

## Birinchi sozlash

```bash
cd src/Bigant.Api
dotnet user-secrets init

# Baza
dotnet user-secrets set "ConnectionStrings:Db" "Host=localhost;Database=bigant;Username=bigant;Password=..."

# JWT kaliti (kamida 32 belgi)
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)"

# Admin paroli — faqat birinchi ishga tushirishda
export BIGANT_ADMIN_PASSWORD='...'

# Telegram (BotFather bergan token)
dotnet user-secrets set "Telegram:BotToken" "..."

# Didox (account manager beradi: @Didox_account)
dotnet user-secrets set "Didox:PartnerToken" "..."
dotnet user-secrets set "Didox:Login" "..."
dotnet user-secrets set "Didox:Password" "..."
```

## Agar sir oshkor bo'lsa

| Sir | Nima qilinadi |
|---|---|
| Telegram bot token | [@BotFather](https://t.me/BotFather) → `/revoke` → yangi token |
| Postgres paroli | `ALTER USER bigant WITH PASSWORD '...';` |
| JWT kaliti | Yangisini qo'ying — barcha tokenlar bekor bo'ladi |
| E-IMZO kaliti (.pfx) | Ro'yxatga olish markaziga murojaat, kalitni bekor qilish |

**E-IMZO kaliti hech qachon serverda saqlanmaydi** — imzo brauzerda qo'yiladi.
