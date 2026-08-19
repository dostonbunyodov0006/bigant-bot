using Bigant.Domain;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Bigant.Api.Auth;
using Bigant.Api.Endpoints;
using Bigant.Didox;
using Bigant.Infrastructure;
using Bigant.Infrastructure.Jobs;
using Bigant.Infrastructure.Notifications;
using Bigant.Infrastructure.Services;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// ── Loglar ──────────────────────────────────────────────────────────
builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    // XAVFSIZLIK: HttpClient loggeri so'rov URL'ini to'liq yozadi va
    // Telegram tokeni URL ichida ketadi (api.telegram.org/bot<TOKEN>/...).
    // Shu sabab log fayliga token tushib qolardi. Warning'dan pastini o'chiramiz.
    .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File("logs/bigant-.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30));

// ── Konfiguratsiya ──────────────────────────────────────────────────
var cs = builder.Configuration.GetConnectionString("Db")
         ?? throw new InvalidOperationException(
             "ConnectionStrings:Db topilmadi. `dotnet user-secrets set \"ConnectionStrings:Db\" \"...\"` qiling.");

builder.Services.Configure<DidoxOptions>(builder.Configuration.GetSection(DidoxOptions.Section));
builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection(TelegramOptions.Section));

var jwtOptions = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>()
                 ?? new JwtOptions { Key = "" };
if (jwtOptions.Key.Length < 32)
    throw new InvalidOperationException(
        "Jwt:Key kamida 32 belgi bo'lishi kerak. user-secrets yoki environment variable da bering.");
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<JwtService>();

// ── Baza ────────────────────────────────────────────────────────────
// snake_case nomlash AppDbContext.OnModelCreating ichida qo'lda qilingan.
builder.Services.AddDbContext<AppDbContext>(o => o
    .UseNpgsql(cs, npg => npg.EnableRetryOnFailure(3)));

// ── Didox rozetkasi ─────────────────────────────────────────────────
// Enabled=false  → NullDidoxClient (tizim Didox'siz to'liq ishlaydi)
// Enabled=true   → DidoxHttpClient (boshqa hech qanday kod o'zgarmaydi)
var didoxOptions = builder.Configuration.GetSection(DidoxOptions.Section).Get<DidoxOptions>() ?? new();
if (didoxOptions.Enabled)
    builder.Services.AddHttpClient<IDidoxClient, DidoxHttpClient>();
else
    builder.Services.AddSingleton<IDidoxClient, NullDidoxClient>();

// ── Servislar ───────────────────────────────────────────────────────
builder.Services.AddScoped<INumberGenerator, NumberGenerator>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IDealService, DealService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IInboxService, InboxService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.Section));
builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();

builder.Services.AddSingleton<INotificationChannel, InAppChannel>();
builder.Services.AddHttpClient<INotificationChannel, TelegramChannel>();
// Bot tinglovchisi: /start bilan foydalanuvchini akkauntga bog'laydi.
builder.Services.AddHttpClient("telegram-poll");
builder.Services.AddHostedService<TelegramBotWorker>();

builder.Services.AddScoped<ReminderJob>();
builder.Services.AddScoped<DigestJob>();
builder.Services.AddScoped<OutboxJob>();
builder.Services.AddScoped<InboxJob>();
builder.Services.AddScoped<StatusSyncJob>();

// ── Fon vazifalari ──────────────────────────────────────────────────
builder.Services.AddHangfire(c => c
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(cs)));
builder.Services.AddHangfireServer(o => o.WorkerCount = 4);

// ── Auth ────────────────────────────────────────────────────────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader().AllowAnyMethod()));

// Parol tanlash hujumiga qarshi — login endpointi cheklanadi.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddFixedWindowLimiter("login", w =>
    {
        w.Window = TimeSpan.FromMinutes(1);
        w.PermitLimit = 10;
        w.QueueLimit = 0;
    });
    o.AddFixedWindowLimiter("api", w =>
    {
        w.Window = TimeSpan.FromMinutes(1);
        w.PermitLimit = 300;
        w.QueueLimit = 0;
    });
});

// Enumlar matn sifatida keladi va ketadi: "Sale", "Esf", "Accountant".
// Raqam kodlar frontend uchun ham, log o'qish uchun ham noqulay.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();

var app = builder.Build();

// ── Xato ishlovchisi: BusinessException → 400 ───────────────────────
app.UseExceptionHandler(x => x.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    if (ex is BusinessException be)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new { code = be.Code, message = be.Message });
        return;
    }

    // Baza cheklovi buzilishi — bu ham foydalanuvchi xatosi.
    // "Ichki xato" deb ko'rsatish bugalterga hech narsa aytmaydi.
    if (ex is DbUpdateException { InnerException: PostgresException pg })
    {
        var (code, msg) = pg.SqlState switch
        {
            "23505" => ("DUPLICATE", pg.ConstraintName switch
            {
                var c when c?.Contains("counterparties_inn") == true
                    => "Bu INN bilan kontragent allaqachon mavjud",
                var c when c?.Contains("users_username") == true
                    => "Bunday login allaqachon band",
                var c when c?.Contains("deals_kind_number") == true
                    => "Bunday raqamli shartnoma allaqachon bor",
                var c when c?.Contains("documents_didox_id") == true
                    => "Bu hujjat allaqachon ro'yxatga olingan",
                var c when c?.Contains("notifications_dedup") == true
                    => "Bu eslatma allaqachon chiqarilgan",
                var c when c?.Contains("vehicles_plate") == true
                    => "Bu davlat raqamli avtomobil allaqachon kiritilgan",
                _ => "Bunday yozuv allaqachon mavjud"
            }),
            "23503" => ("BAD_REFERENCE",
                "Bog'langan yozuv topilmadi — ko'rsatilgan kontragent, tovar yoki xodim mavjud emas"),
            "23502" => ("REQUIRED_FIELD",
                "Majburiy maydon to'ldirilmagan"),
            "23514" => ("CHECK_FAILED",
                "Qiymat ruxsat etilgan chegaradan tashqarida"),
            _ => ("DB_ERROR", "Ma'lumotlar bazasi amalni rad etdi")
        };

        Log.Warning(ex, "Baza cheklovi: {State} {Constraint}", pg.SqlState, pg.ConstraintName);
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new { code, message = msg });
        return;
    }

    // Noto'g'ri JSON / yetishmayotgan maydon — bu foydalanuvchi xatosi, 500 emas.
    if (ex is BadHttpRequestException bad)
    {
        Log.Warning(ex, "So'rov tanasi noto'g'ri");
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        await ctx.Response.WriteAsJsonAsync(new
        {
            code = "BAD_REQUEST",
            message = "So'rov tanasi noto'g'ri: " + (bad.InnerException?.Message ?? bad.Message)
        });
        return;
    }

    Log.Error(ex, "Kutilmagan xato");
    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await ctx.Response.WriteAsJsonAsync(new { code = "INTERNAL", message = "Ichki xato yuz berdi" });
}));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseSerilogRequestLogging();
app.UseRateLimiter();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapDirectoryEndpoints();
app.MapCatalogEndpoints();
app.MapDealEndpoints();
app.MapDocumentEndpoints();
app.MapPaymentEndpoints();
app.MapInboxEndpoints();
app.MapNotificationEndpoints();
app.MapDashboardEndpoints();
app.MapRoleDashboardEndpoints();
app.MapRateEndpoints();
app.MapImportEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "ok", didox = didoxOptions.Enabled }))
   .AllowAnonymous();

// Hangfire paneli — faqat lokalda ochiq. Prodda auth filtri qo'shing.
if (app.Environment.IsDevelopment())
    app.MapHangfireDashboard("/jobs");

// ── Jadval bo'yicha ishlaydigan vazifalar ───────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var log = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await DbSeeder.SeedAsync(db, log, Environment.GetEnvironmentVariable("BIGANT_ADMIN_PASSWORD"));
}

var jobs = app.Services.GetRequiredService<IRecurringJobManager>();
// Vaqt mintaqasi UzTime dan olinadi — u Windows/macOS/Linux uchun fallback bilan
// himoyalangan. To'g'ridan-to'g'ri FindSystemTimeZoneById chaqirilsa nomi
// topilmagan tizimda ilova UMUMAN ISHGA TUSHMAYDI.
var uz = UzTime.Zone;

jobs.AddOrUpdate<ReminderJob>("eslatmalar", j => j.RunAsync(CancellationToken.None), "*/30 * * * *");
jobs.AddOrUpdate<DigestJob>("kunlik-xulosa", j => j.RunAsync(CancellationToken.None), "0 9 * * *",
    new RecurringJobOptions { TimeZone = uz });
jobs.AddOrUpdate<OutboxJob>("chiquvchi-navbat", j => j.RunAsync(CancellationToken.None), "* * * * *");
jobs.AddOrUpdate<InboxJob>("kiruvchi-hujjatlar", j => j.RunAsync(CancellationToken.None),
    $"*/{Math.Max(1, didoxOptions.InboxPollMinutes)} * * * *");
jobs.AddOrUpdate<StatusSyncJob>("holat-sinxronizatsiya", j => j.RunAsync(CancellationToken.None), "*/15 * * * *");

app.Run();
