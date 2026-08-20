using Bigant.Domain.Entities;
using Bigant.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bigant.Infrastructure;

public static class DbSeeder
{
    /// <summary>
    /// Birinchi ishga tushirishda minimal ma'lumot.
    /// Admin paroli environment o'zgaruvchisidan olinadi — kodda saqlanmaydi.
    /// </summary>
    public static async Task SeedAsync(AppDbContext db, ILogger log, string? adminPassword,
                                       CancellationToken ct = default)
    {
        await db.Database.MigrateAsync(ct);

        await ResetFromEnvAsync(db, log, ct);

        if (!await db.Users.AnyAsync(ct))
        {
            if (string.IsNullOrWhiteSpace(adminPassword))
            {
                log.LogWarning(
                    "Admin yaratilmadi: BIGANT_ADMIN_PASSWORD o'rnatilmagan. " +
                    "Terminalda qo'ying va qayta ishga tushiring.");
            }
            else
            {
                db.Users.Add(new AppUser
                {
                    Username = "admin",
                    FullName = "Administrator",
                    Role = UserRole.Admin,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword)
                });
                await db.SaveChangesAsync(ct);
                log.LogInformation("Admin foydalanuvchi yaratildi (login: admin)");
            }
        }

        if (!await db.CompanyProfiles.AnyAsync(ct))
        {
            // Bo'sh karkas — rahbar Sozlamalar ekranidan to'ldiradi.
            // Didox bularsiz hech qanday hujjatni qabul qilmaydi.
            db.CompanyProfiles.Add(new CompanyProfile
            {
                Name = "\"BIGANT\" MCHJ",
                Tin = "",
                Address = "Toshkent shahri, Dombirobod",
                TaxpayerType = 10
            });
            await db.SaveChangesAsync(ct);
            log.LogWarning(
                "Kompaniya rekvizitlari bo'sh yaratildi. Didox'dan oldin to'ldiring: " +
                "INN, direktor F.I.Sh va PINFL, h/r, MFO, OKED.");
        }

        if (!await db.ContractTemplates.AnyAsync(ct))
        {
            // Didox dogovorni Parts[] siz qabul qilmaydi.
            // O'rinbosarlar shartnoma yaratilganda almashtiriladi.
            db.ContractTemplates.Add(new ContractTemplate
            {
                Name = "Standart yetkazib berish shartnomasi",
                IsDefault = true,
                Parts =
                [
                    new ContractTemplatePart { OrdNo = 1, Title = "1. Shartnoma predmeti",
                        Body = "Yetkazib beruvchi {BIZ} tovarni yetkazib berish, Xaridor {KLIENT} esa " +
                               "uni qabul qilib to'lash majburiyatini oladi. Tovar nomi, miqdori va " +
                               "narxi shartnomaga ilova qilingan spetsifikatsiyada ko'rsatilgan." },
                    new ContractTemplatePart { OrdNo = 2, Title = "2. Shartnoma summasi",
                        Body = "Shartnoma umumiy summasi {SUMMA} so'mni tashkil etadi (QQS bilan)." },
                    new ContractTemplatePart { OrdNo = 3, Title = "3. To'lov tartibi",
                        Body = "To'lov {MUDDAT} kun ichida Yetkazib beruvchining hisob raqamiga " +
                               "pul o'tkazish yo'li bilan amalga oshiriladi." },
                    new ContractTemplatePart { OrdNo = 4, Title = "4. Yetkazib berish sharti",
                        Body = "Tovar Xaridorning ishonchnomasi asosida beriladi. " +
                               "Tovarga bo'lgan mulk huquqi TTN imzolangan paytdan o'tadi." },
                    new ContractTemplatePart { OrdNo = 5, Title = "5. Tomonlar javobgarligi",
                        Body = "Majburiyatlar bajarilmaganda tomonlar O'zbekiston Respublikasi " +
                               "qonunchiligiga muvofiq javobgar bo'ladilar." },
                    new ContractTemplatePart { OrdNo = 6, Title = "6. Amal qilish muddati",
                        Body = "Shartnoma {SANA} dan kuchga kiradi va tomonlar majburiyatlarini " +
                               "to'liq bajarguniga qadar amal qiladi." }
                ]
            });
            await db.SaveChangesAsync(ct);
            log.LogInformation("Standart shartnoma shabloni qo'shildi (Didox Parts[] uchun)");
        }
    }

    /// <summary>
    /// ENG OXIRGI CHORA: hamma parol yo'qolganda.
    ///
    /// Rahbar o'z parolini unutsa, odatda Telegramga kod keladi. Lekin
    /// Telegram ham ulanmagan bo'lsa, tizimga kirishning yo'li qolmaydi —
    /// rahbardan yuqorida hech kim yo'q. Shu holat uchun serverning o'zida
    /// bir martalik yo'l:
    ///
    ///     BIGANT_RESET_PASSWORD="admin:YangiParol2026"
    ///
    /// O'zgaruvchi qo'yilib server ishga tushirilsa, shu xodimning paroli
    /// almashadi. Keyin o'zgaruvchini OLIB TASHLASH kerak — aks holda har
    /// qayta ishga tushishda parol o'sha holatga qaytaveradi.
    /// Serverga kira oladigan odam baribir bazani ham ko'ra oladi, shuning
    /// uchun bu yangi teshik ochmaydi.
    /// </summary>
    private static async Task ResetFromEnvAsync(AppDbContext db, ILogger log, CancellationToken ct)
    {
        var raw = Environment.GetEnvironmentVariable("BIGANT_RESET_PASSWORD");
        if (string.IsNullOrWhiteSpace(raw)) return;

        var sep = raw.IndexOf(':');
        if (sep <= 0 || sep == raw.Length - 1)
        {
            log.LogWarning("BIGANT_RESET_PASSWORD noto'g'ri yozilgan. Kutilgan ko'rinish: login:parol");
            return;
        }

        var login = raw[..sep].Trim();
        var pass  = raw[(sep + 1)..];

        if (pass.Length < 8)
        {
            log.LogWarning("BIGANT_RESET_PASSWORD: parol kamida 8 belgi bo'lsin. Parol almashtirilmadi.");
            return;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == login, ct);
        if (user is null)
        {
            log.LogWarning("BIGANT_RESET_PASSWORD: «{Login}» degan xodim yo'q", login);
            return;
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(pass);
        user.FailedLogins = 0;
        user.LockedUntil = null;
        user.IsActive = true;
        await db.SaveChangesAsync(ct);

        log.LogWarning("«{Login}» paroli BIGANT_RESET_PASSWORD orqali almashtirildi. "
                     + "O'zgaruvchini endi olib tashlang.", login);
    }
}
