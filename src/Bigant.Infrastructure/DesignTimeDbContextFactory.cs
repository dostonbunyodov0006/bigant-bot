using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Bigant.Infrastructure;

/// <summary>
/// Migratsiya yaratish uchun. `dotnet ef` butun ilovani ko'tarmaydi —
/// shuning uchun JWT kaliti, Hangfire va boshqa sozlamalar talab qilinmaydi.
///
/// Ulanish satri quyidagi tartibda qidiriladi:
///   1. BIGANT_DB environment variable
///   2. Bigant.Api ning user-secrets fayli
///   3. appsettings.json
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("BIGANT_DB");

        if (string.IsNullOrWhiteSpace(cs))
        {
            // user-secrets BIRINCHI o'qiladi — appsettings.json dagi bo'sh parol
            // haqiqiy sirni bosib qolmasligi uchun.
            var secrets = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "UserSecrets", "378a2198-64d3-4888-ba60-58780fbcfb4d", "secrets.json");

            var cfg = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddJsonFile(secrets, optional: true)
                .AddEnvironmentVariables()
                .Build();

            cs = cfg.GetConnectionString("Db");
        }

        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                "Ulanish satri topilmadi. BIGANT_DB environment variable bering yoki " +
                "src/Bigant.Api da `dotnet user-secrets set \"ConnectionStrings:Db\" \"...\"` qiling.");

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(cs)
            .Options;

        return new AppDbContext(options);
    }
}
