using Bigant.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Bigant.Infrastructure;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Counterparty> Counterparties => Set<Counterparty>();
    public DbSet<Deal> Deals => Set<Deal>();
    public DbSet<DealStep> DealSteps => Set<DealStep>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<OutboxItem> Outbox => Set<OutboxItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NumberCounter> NumberCounters => Set<NumberCounter>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<DoverennostInfo> Doverennosti => Set<DoverennostInfo>();
    public DbSet<TtnInfo> Ttns => Set<TtnInfo>();
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<ContractPart> ContractParts => Set<ContractPart>();
    public DbSet<ContractTemplate> ContractTemplates => Set<ContractTemplate>();
    public DbSet<ContractTemplatePart> ContractTemplateParts => Set<ContractTemplatePart>();
    public DbSet<DocumentFile> DocumentFiles => Set<DocumentFile>();
    public DbSet<IntegrationLog> IntegrationLogs => Set<IntegrationLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // 5000+ tovarni nomi bo'yicha tez qidirish uchun.
        b.HasPostgresExtension("pg_trgm");

        // Enumlarni matn sifatida saqlaymiz — migratsiya sodda, o'qish oson.
        foreach (var et in b.Model.GetEntityTypes())
            foreach (var p in et.GetProperties().Where(p => p.ClrType.IsEnum ||
                     (Nullable.GetUnderlyingType(p.ClrType)?.IsEnum ?? false)))
                p.SetProviderClrType(typeof(string));

        // ── Users ───────────────────────────────────────────────
        b.Entity<AppUser>(e =>
        {
            e.ToTable("users");
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(64).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Role).HasMaxLength(32).HasConversion<string>();
            e.Property(x => x.TelegramChatId).HasMaxLength(64);
        });

        // ── Counterparties ──────────────────────────────────────
        b.Entity<Counterparty>(e =>
        {
            e.ToTable("counterparties");
            e.Property(x => x.Name).HasMaxLength(300).IsRequired();
            e.Property(x => x.Inn).HasMaxLength(9);
            e.Property(x => x.Pinfl).HasMaxLength(14);
            e.Property(x => x.BankAccount).HasMaxLength(24);
            e.Property(x => x.BankMfo).HasMaxLength(5);
            e.Property(x => x.DirectorPinfl).HasMaxLength(14);
            e.Property(x => x.AccountantName).HasMaxLength(200);
            e.Property(x => x.AccountantPinfl).HasMaxLength(14);
            e.Property(x => x.VatRegCode).HasMaxLength(32);
            e.Property(x => x.VatRegStatus).HasMaxLength(16);
            e.Property(x => x.BranchCode).HasMaxLength(16);
            e.Property(x => x.BranchName).HasMaxLength(200);
            e.Property(x => x.RegionName).HasMaxLength(120);
            e.Property(x => x.DistrictName).HasMaxLength(120);
            e.Property(x => x.ContactPerson).HasMaxLength(200);
            e.Property(x => x.ContactPhone).HasMaxLength(32);
            e.Property(x => x.WorkPhone).HasMaxLength(32);
            e.Ignore(x => x.IsReadyForDidox);
            e.HasIndex(x => x.Inn).IsUnique().HasFilter("inn IS NOT NULL");
            e.HasIndex(x => x.Name);
        });

        // ── Catalog ─────────────────────────────────────────────


        // ── Deals ───────────────────────────────────────────────
        b.Entity<Deal>(e =>
        {
            e.ToTable("deals");
            e.Property(x => x.Number).HasMaxLength(32).IsRequired();
            e.Property(x => x.Kind).HasMaxLength(16).HasConversion<string>();
            e.Property(x => x.Status).HasMaxLength(16).HasConversion<string>();
            e.Property(x => x.AmountTotal).HasPrecision(18, 2);
            e.Property(x => x.AmountPaid).HasPrecision(18, 2);
            e.Property(x => x.PrepayPercent).HasPrecision(5, 2);
            e.Property(x => x.ContractName).HasMaxLength(300);
            e.Property(x => x.ContractPlace).HasMaxLength(200);
            e.Property(x => x.DeliveryAddress).HasMaxLength(500);
            e.Property(x => x.DeliveryTerms).HasMaxLength(200);
            e.Property(x => x.Comment).HasMaxLength(2000);
            e.Ignore(x => x.AmountUnpaid);
            e.Ignore(x => x.AgeDays);
            e.Ignore(x => x.AllRequiredStepsDone);

            e.HasIndex(x => new { x.Kind, x.Number }).IsUnique();
            e.HasIndex(x => new { x.Status, x.CreatedAt });
            e.HasIndex(x => x.ManagerId);

            e.HasOne(x => x.Counterparty).WithMany().HasForeignKey(x => x.CounterpartyId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Manager).WithMany().HasForeignKey(x => x.ManagerId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ParentDeal).WithMany(x => x.ChildDeals).HasForeignKey(x => x.ParentDealId)
                .OnDelete(DeleteBehavior.Restrict);
        });


        b.Entity<DealStep>(e =>
        {
            e.ToTable("deal_steps");
            e.Property(x => x.Step).HasMaxLength(32).HasConversion<string>();
            e.Property(x => x.Status).HasMaxLength(16).HasConversion<string>();
            e.Property(x => x.Note).HasMaxLength(1000);
            e.Ignore(x => x.IsOverdue);
            e.HasIndex(x => new { x.DealId, x.Step }).IsUnique();
            e.HasIndex(x => new { x.Status, x.DueDate });
            e.HasOne(x => x.Deal).WithMany(d => d.Steps).HasForeignKey(x => x.DealId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── Documents ───────────────────────────────────────────
        b.Entity<Document>(e =>
        {
            e.ToTable("documents");
            e.Property(x => x.DocType).HasMaxLength(32).HasConversion<string>();
            e.Property(x => x.Direction).HasMaxLength(8).HasConversion<string>();
            e.Property(x => x.Status).HasMaxLength(16).HasConversion<string>();
            e.Property(x => x.DocNumber).HasMaxLength(64);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.VatAmount).HasPrecision(18, 2);
            e.Property(x => x.DidoxId).HasMaxLength(128);
            e.Property(x => x.RejectReason).HasMaxLength(1000);
            e.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            e.Ignore(x => x.IsSignable);
            e.Ignore(x => x.IsSendable);

            e.HasIndex(x => x.DidoxId).IsUnique().HasFilter("didox_id IS NOT NULL");
            e.HasIndex(x => new { x.Status, x.DocType });
            e.HasIndex(x => x.DealId);

            e.HasOne(x => x.Deal).WithMany(d => d.Documents).HasForeignKey(x => x.DealId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Counterparty).WithMany().HasForeignKey(x => x.CounterpartyId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ParentDocument).WithMany().HasForeignKey(x => x.ParentDocumentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<OutboxItem>(e =>
        {
            e.ToTable("didox_outbox");
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.HasIndex(x => x.NextTryAt).HasFilter("sent_at IS NULL");
            e.Property(x => x.LastError).HasMaxLength(4000);
            e.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Payments ────────────────────────────────────────────
        b.Entity<Payment>(e =>
        {
            e.ToTable("payments");
            e.Property(x => x.Direction).HasMaxLength(8).HasConversion<string>();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.BankDocNo).HasMaxLength(64);
            e.Property(x => x.Purpose).HasMaxLength(1000);
            e.Property(x => x.MatchedBy).HasMaxLength(16);
            e.HasIndex(x => new { x.CounterpartyId, x.PayDate });
            e.HasOne(x => x.Counterparty).WithMany().HasForeignKey(x => x.CounterpartyId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Deal).WithMany(d => d.Payments).HasForeignKey(x => x.DealId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── Notifications ───────────────────────────────────────
        b.Entity<Notification>(e =>
        {
            e.ToTable("notifications");
            e.Property(x => x.Kind).HasMaxLength(32).HasConversion<string>();
            e.Property(x => x.Severity).HasMaxLength(16).HasConversion<string>();
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            e.Property(x => x.DedupKey).HasMaxLength(200).IsRequired();
            // Bitta eslatma ikki marta chiqmasligining kafolati.
            e.HasIndex(x => x.DedupKey).IsUnique();
            e.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Bigant rekvizitlari (bitta yozuv) ──
        b.Entity<CompanyProfile>(e =>
        {
            e.ToTable("company_profile");
            e.Property(x => x.Name).HasMaxLength(300).IsRequired();
            e.Property(x => x.Tin).HasMaxLength(9).IsRequired();
            e.Property(x => x.BranchCode).HasMaxLength(16);
            e.Property(x => x.BranchName).HasMaxLength(200);
            e.Property(x => x.Account).HasMaxLength(24);
            e.Property(x => x.BankId).HasMaxLength(5);
            e.Property(x => x.BankName).HasMaxLength(200);
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.RegionName).HasMaxLength(120);
            e.Property(x => x.DistrictName).HasMaxLength(120);
            e.Property(x => x.DirectorName).HasMaxLength(200);
            e.Property(x => x.DirectorPinfl).HasMaxLength(14);
            e.Property(x => x.AccountantName).HasMaxLength(200);
            e.Property(x => x.AccountantPinfl).HasMaxLength(14);
            e.Property(x => x.VatRegCode).HasMaxLength(32);
            e.Property(x => x.VatRegStatus).HasMaxLength(16);
            e.Property(x => x.WorkPhone).HasMaxLength(32);
            e.Property(x => x.Mobile).HasMaxLength(32);
            e.Property(x => x.Email).HasMaxLength(120);
            e.Property(x => x.WarehouseAddress).HasMaxLength(500);
            e.Property(x => x.TelegramChatId).HasMaxLength(64);
            e.Ignore(x => x.IsReadyForDidox);
        });

        // ── Shartnoma bo'limlari (Didox Parts[]) ──
        b.Entity<ContractPart>(e =>
        {
            e.ToTable("contract_parts");
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Body).IsRequired();
            e.HasIndex(x => new { x.DealId, x.OrdNo });
            e.HasOne(x => x.Deal).WithMany(d => d.Parts).HasForeignKey(x => x.DealId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ContractTemplate>(e =>
        {
            e.ToTable("contract_templates");
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });

        b.Entity<ContractTemplatePart>(e =>
        {
            e.ToTable("contract_template_parts");
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Body).IsRequired();
            e.HasOne(x => x.Template).WithMany(t => t.Parts).HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Xodimlar va avtomobillar (doverennost / TTN uchun) ──
        b.Entity<Employee>(e =>
        {
            e.ToTable("employees");
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Position).HasMaxLength(120);
            e.Property(x => x.PassportSeries).HasMaxLength(4);
            e.Property(x => x.PassportNumber).HasMaxLength(12);
            e.Property(x => x.PassportIssuedBy).HasMaxLength(300);
            e.Property(x => x.Pinfl).HasMaxLength(14);
            e.Property(x => x.Phone).HasMaxLength(32);
            e.Property(x => x.DriverLicense).HasMaxLength(32);
            e.Ignore(x => x.CanReceiveGoods);
            e.HasIndex(x => x.FullName);
        });

        b.Entity<Vehicle>(e =>
        {
            e.ToTable("vehicles");
            e.Property(x => x.Model).HasMaxLength(120).IsRequired();
            e.Property(x => x.PlateNumber).HasMaxLength(24).IsRequired();
            e.Property(x => x.TrailerNumber).HasMaxLength(24);
            e.Property(x => x.CapacityTons).HasPrecision(10, 3);
            e.Property(x => x.CarrierName).HasMaxLength(300);
            e.Property(x => x.CarrierInn).HasMaxLength(9);
            e.HasIndex(x => x.PlateNumber).IsUnique();
        });

        b.Entity<DoverennostInfo>(e =>
        {
            e.ToTable("doverennost_info");
            e.Property(x => x.EmployeeName).HasMaxLength(200).IsRequired();
            e.Property(x => x.EmployeePinfl).HasMaxLength(14).IsRequired();
            e.Property(x => x.Position).HasMaxLength(120);
            e.Property(x => x.PassportSeries).HasMaxLength(4);
            e.Property(x => x.PassportNumber).HasMaxLength(12);
            e.Property(x => x.PassportIssuedBy).HasMaxLength(300);
            e.Property(x => x.SupplierName).HasMaxLength(300);
            e.Property(x => x.BasisDocument).HasMaxLength(200);
            e.Ignore(x => x.IsExpired);
            e.HasIndex(x => x.DocumentId).IsUnique();
            e.HasIndex(x => x.ValidUntil);
            e.HasOne(x => x.Document).WithOne(d => d.Doverennost)
                .HasForeignKey<DoverennostInfo>(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<TtnInfo>(e =>
        {
            e.ToTable("ttn_info");
            e.Property(x => x.DriverName).HasMaxLength(200).IsRequired();
            e.Property(x => x.DriverPinfl).HasMaxLength(14);
            e.Property(x => x.DriverLicense).HasMaxLength(32);
            e.Property(x => x.VehicleModel).HasMaxLength(120).IsRequired();
            e.Property(x => x.VehicleNumber).HasMaxLength(24).IsRequired();
            e.Property(x => x.TrailerNumber).HasMaxLength(24);
            e.Property(x => x.TrailerModel).HasMaxLength(120);
            e.Property(x => x.LoadAddress).HasMaxLength(400).IsRequired();
            e.Property(x => x.LoadRegionName).HasMaxLength(120);
            e.Property(x => x.LoadDistrictName).HasMaxLength(120);
            e.Property(x => x.UnloadAddress).HasMaxLength(400).IsRequired();
            e.Property(x => x.UnloadRegionName).HasMaxLength(120);
            e.Property(x => x.UnloadDistrictName).HasMaxLength(120);
            e.Property(x => x.DistanceKm).HasPrecision(10, 2);
            e.Property(x => x.WeightBrutto).HasPrecision(10, 3);
            e.Property(x => x.WeightNetto).HasPrecision(10, 3);
            e.Property(x => x.CarrierName).HasMaxLength(300);
            e.Property(x => x.CarrierInn).HasMaxLength(9);
            e.Property(x => x.FreightCost).HasPrecision(18, 2);
            e.Property(x => x.ResponsibleName).HasMaxLength(200);
            e.Property(x => x.ResponsiblePinfl).HasMaxLength(14);
            e.Ignore(x => x.IsReadyForDidox);
            e.HasIndex(x => x.DocumentId).IsUnique();
            e.HasOne(x => x.Document).WithOne(d => d.Ttn)
                .HasForeignKey<TtnInfo>(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Driver).WithMany().HasForeignKey(x => x.DriverId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Vehicle).WithMany().HasForeignKey(x => x.VehicleId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── Fayllar va integratsiya jurnali ─────────────────────
        b.Entity<DocumentFile>(e =>
        {
            e.ToTable("document_files");
            e.Property(x => x.FileName).HasMaxLength(300).IsRequired();
            e.Property(x => x.MimeType).HasMaxLength(120).IsRequired();
            e.Property(x => x.StorageKey).HasMaxLength(500).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.Kind).HasMaxLength(32).IsRequired();
            e.HasIndex(x => x.DocumentId);
            e.HasOne(x => x.Document).WithMany().HasForeignKey(x => x.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<IntegrationLog>(e =>
        {
            e.ToTable("integration_logs");
            e.Property(x => x.System).HasMaxLength(32).IsRequired();
            e.Property(x => x.Operation).HasMaxLength(64).IsRequired();
            e.Property(x => x.RequestUrl).HasMaxLength(1000);
            e.Property(x => x.Error).HasMaxLength(4000);
            e.HasIndex(x => new { x.System, x.At });
            e.HasIndex(x => x.DocumentId);
            e.HasIndex(x => x.CorrelationId);
        });

        // ── Valyuta kursi ────────────────────────────────────────

        // ── Support ─────────────────────────────────────────────
        b.Entity<NumberCounter>(e =>
        {
            e.ToTable("number_counters");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(64);
        });

        b.Entity<AuditEntry>(e =>
        {
            e.ToTable("audit_log");
            e.Property(x => x.Entity).HasMaxLength(64).IsRequired();
            e.Property(x => x.Action).HasMaxLength(64).IsRequired();
            e.Property(x => x.OldValue).HasColumnType("jsonb");
            e.Property(x => x.NewValue).HasColumnType("jsonb");
            e.HasIndex(x => new { x.Entity, x.EntityId });
            e.HasIndex(x => x.At);
        });

        // snake_case nomlar — Postgres uslubi
        foreach (var entity in b.Model.GetEntityTypes())
        {
            foreach (var prop in entity.GetProperties())
                prop.SetColumnName(ToSnake(prop.GetColumnName()));
            foreach (var key in entity.GetKeys())
                key.SetName(ToSnake(key.GetName()!));
            foreach (var fk in entity.GetForeignKeys())
                fk.SetConstraintName(ToSnake(fk.GetConstraintName()!));
            foreach (var ix in entity.GetIndexes())
                ix.SetDatabaseName(ToSnake(ix.GetDatabaseName()!));
        }
    }

    private static string ToSnake(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var sb = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && name[i - 1] != '_') sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
