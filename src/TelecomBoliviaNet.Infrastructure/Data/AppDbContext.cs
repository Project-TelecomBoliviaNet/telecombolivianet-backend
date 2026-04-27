using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Domain.Entities.Admin;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Audit;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Dashboard;
using TelecomBoliviaNet.Domain.Entities.Installations;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Entities.Plans;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Entities.Payments;

namespace TelecomBoliviaNet.Infrastructure.Data;

public class AppDbContext : DbContext
{
    // ── Módulo 1: Auth ────────────────────────────────────────────────────────
    public DbSet<UserSystem>     UserSystems     { get; set; }
    public DbSet<TokenBlacklist> TokenBlacklists { get; set; }
    public DbSet<RefreshToken>   RefreshTokens   { get; set; }
    public DbSet<AuditLog>       AuditLogs       { get; set; }

    // ── Módulo 2: Clientes ────────────────────────────────────────────────────
    public DbSet<Plan>           Plans           { get; set; }
    public DbSet<TbnSequence>    TbnSequences    { get; set; }
    public DbSet<Client>         Clients         { get; set; }
    public DbSet<Invoice>        Invoices        { get; set; }
    public DbSet<Payment>        Payments        { get; set; }
    public DbSet<PaymentInvoice>   PaymentInvoices  { get; set; }
    public DbSet<ClientAttachment>   ClientAttachments  { get; set; }  // US-CLI-ADJUNTOS M5
    // M9
    public DbSet<TicketAttachment>  TicketAttachments  { get; set; }  // US-TKT-ADJ
    public DbSet<TicketSequence>    TicketSequences    { get; set; }  // US-TKT-CORRELATIVO
    // M2 nuevas entidades
    public DbSet<CashClose>        CashCloses       { get; set; }  // US-PAG-CAJA
    public DbSet<PaymentReceipt>   PaymentReceipts  { get; set; }  // US-PAG-RECIBO
    public DbSet<InvoiceSequence>  InvoiceSequences { get; set; }  // US-FAC-CORRELATIVO
    public DbSet<ReceiptSequence>  ReceiptSequences { get; set; }  // US-PAG-RECIBO

    // ── Módulo 7: Tickets (extendido — US-01 a US-21) ─────────────────────────
    public DbSet<SupportTicket>       SupportTickets        { get; set; }
    public DbSet<TicketNotification>  TicketNotifications   { get; set; }
    public DbSet<TicketComment>       TicketComments        { get; set; }
    public DbSet<TicketWorkLog>       TicketWorkLogs        { get; set; }
    public DbSet<TicketVisit>         TicketVisits          { get; set; }
    public DbSet<SlaPlan>             SlaPlans              { get; set; }
    public DbSet<WhatsAppReceipt>     WhatsAppReceipts      { get; set; }

    // ── Módulo 8a: Dashboard ──────────────────────────────────────────────────
    public DbSet<DashboardPreference> DashboardPreferences { get; set; }

    // ── Módulo 8b: Notificaciones WhatsApp ────────────────────────────────────
    // M7
    public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }

    public DbSet<NotifConfig>             NotifConfigs            { get; set; }
    public DbSet<NotifPlantilla>          NotifPlantillas         { get; set; }
    public DbSet<NotifPlantillaHistorial> NotifPlantillaHistorial { get; set; }
    public DbSet<NotifOutbox>             NotifOutbox             { get; set; }
    public DbSet<NotifLog>                NotifLogs               { get; set; }
    // US-NOT-02
    public DbSet<NotifSegment>            NotifSegments           { get; set; }

    // ── Módulo Instalaciones ──────────────────────────────────────────────────
    public DbSet<Installation>       Installations      { get; set; }

    // ── Módulo QR de clientes ─────────────────────────────────────────────────
    public DbSet<ClientQr>           ClientQrs          { get; set; }

    // ── Módulo Cambio de Plan ─────────────────────────────────────────────────
    public DbSet<PlanChangeRequest>  PlanChangeRequests { get; set; }

    // ── Módulo Admin: Configuración de sistema ────────────────────────────────
    public DbSet<SystemConfig>       SystemConfigs      { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // ── UserSystem ────────────────────────────────────────────────────────
        mb.Entity<UserSystem>(e =>
        {
            e.ToTable("UserSystems");
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.FullName).HasMaxLength(150).IsRequired();
            e.Property(u => u.Email).HasMaxLength(200).IsRequired();
            e.Property(u => u.PasswordHash).IsRequired();
            e.Property(u => u.Role).HasConversion<string>().HasMaxLength(30);
            e.Property(u => u.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(u => u.Phone).HasMaxLength(30); // nullable por defecto
            // BUG FIX: filtro global de soft delete — usuarios dados de baja
            // no deben ser visibles en ninguna query de la aplicación.
            e.Property(u => u.IsDeleted).HasDefaultValue(false).IsRequired();
            e.HasIndex(u => u.IsDeleted);
            e.HasQueryFilter(u => !u.IsDeleted);
        });

        // ── TokenBlacklist ────────────────────────────────────────────────────
        mb.Entity<TokenBlacklist>(e =>
        {
            e.ToTable("TokenBlacklists");
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.Token);
            e.HasIndex(t => t.ExpiresAt);
        });

        // ── AuditLog ──────────────────────────────────────────────────────────
        mb.Entity<AuditLog>(e =>
        {
            e.ToTable("AuditLogs");
            e.HasKey(a => a.Id);
            e.HasIndex(a => a.UserId);
            e.HasIndex(a => a.CreatedAt);
            e.HasIndex(a => a.Action);
            e.Property(a => a.Module).HasMaxLength(50);
            e.Property(a => a.Action).HasMaxLength(50);
        });

        // ── Plan ──────────────────────────────────────────────────────────────
        mb.Entity<Plan>(e =>
        {
            e.ToTable("Plans");
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).HasMaxLength(100).IsRequired();
            e.Property(p => p.MonthlyPrice).HasColumnType("decimal(10,2)");
        });

        // ── TbnSequence ───────────────────────────────────────────────────────
        mb.Entity<TbnSequence>(e =>
        {
            e.ToTable("TbnSequences");
            e.HasKey(t => t.Id);
            e.Property(t => t.Prefix).HasMaxLength(10);
        });

        // ── Client ────────────────────────────────────────────────────────────
        mb.Entity<Client>(e =>
        {
            e.ToTable("Clients");
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.TbnCode).IsUnique();
            e.HasIndex(c => c.IdentityCard).IsUnique();
            e.HasIndex(c => c.PhoneMain);
            e.Property(c => c.TbnCode).HasMaxLength(20).IsRequired();
            e.Property(c => c.FullName).HasMaxLength(150).IsRequired();
            e.Property(c => c.IdentityCard).HasMaxLength(20).IsRequired();
            e.Property(c => c.PhoneMain).HasMaxLength(20).IsRequired();
            e.Property(c => c.Zone).HasMaxLength(100).IsRequired();
            e.Property(c => c.WinboxNumber).HasMaxLength(50).IsRequired();
            e.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(c => c.GpsLatitude).HasColumnType("decimal(10,7)");
            e.Property(c => c.GpsLongitude).HasColumnType("decimal(10,7)");

            // Soft delete — columnas
            e.Property(c => c.IsDeleted).HasDefaultValue(false).IsRequired();
            e.HasIndex(c => c.IsDeleted); // índice para el filtro global

            // Filtro global: excluye registros eliminados de TODAS las queries EF Core.
            // Para consultar eliminados usar .IgnoreQueryFilters() explícitamente.
            e.HasQueryFilter(c => !c.IsDeleted);

            e.HasOne(c => c.Plan)
             .WithMany()
             .HasForeignKey(c => c.PlanId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(c => c.InstalledBy)
             .WithMany()
             .HasForeignKey(c => c.InstalledByUserId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Invoice ───────────────────────────────────────────────────────────
        mb.Entity<Invoice>(e =>
        {
            e.ToTable("Invoices");
            e.HasKey(i => i.Id);
            e.HasIndex(i => new { i.ClientId, i.Year, i.Month, i.Type }).IsUnique();
            e.HasIndex(i => i.Status);
            e.HasIndex(i => new { i.ClientId, i.Status });
            e.Property(i => i.Amount).HasColumnType("decimal(10,2)");
            e.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.Type).HasConversion<string>().HasMaxLength(20);

            // Soft delete
            e.Property(i => i.IsDeleted).HasDefaultValue(false).IsRequired();
            e.HasQueryFilter(i => !i.IsDeleted);

            e.HasOne(i => i.Client)
             .WithMany(c => c.Invoices)
             .HasForeignKey(i => i.ClientId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Payment ───────────────────────────────────────────────────────────
        mb.Entity<Payment>(e =>
        {
            e.ToTable("Payments");
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.ClientId);
            e.HasIndex(p => p.PaidAt);
            e.HasIndex(p => new { p.IsVoided, p.PaidAt });
            e.Property(p => p.Amount).HasColumnType("decimal(10,2)");
            e.Property(p => p.Method).HasConversion<string>().HasMaxLength(30);

            // Soft delete
            e.Property(p => p.IsDeleted).HasDefaultValue(false).IsRequired();
            e.HasQueryFilter(p => !p.IsDeleted);

            e.HasOne(p => p.Client)
             .WithMany(c => c.Payments)
             .HasForeignKey(p => p.ClientId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(p => p.RegisteredBy)
             .WithMany()
             .HasForeignKey(p => p.RegisteredByUserId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── PaymentInvoice ────────────────────────────────────────────────────
        mb.Entity<PaymentInvoice>(e =>
        {
            e.ToTable("PaymentInvoices");
            e.HasKey(pi => pi.Id);
            e.HasIndex(pi => new { pi.PaymentId, pi.InvoiceId }).IsUnique();

            e.HasOne(pi => pi.Payment)
             .WithMany(p => p.PaymentInvoices)
             .HasForeignKey(pi => pi.PaymentId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(pi => pi.Invoice)
             .WithMany(i => i.PaymentInvoices)
             .HasForeignKey(pi => pi.InvoiceId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── SupportTicket (extendido — US-01 a US-21) ─────────────────────────
        mb.Entity<SupportTicket>(e =>
        {
            e.ToTable("SupportTickets");
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.Status);
            e.HasIndex(t => t.Priority);
            e.HasIndex(t => t.ClientId);
            e.HasIndex(t => t.AssignedToUserId);
            e.HasIndex(t => new { t.Status, t.Priority });
            // BUG FIX: filtro global para excluir tickets de clientes dados de baja.
            // Evita que los tickets de clientes soft-deleted aparezcan en listados y KPIs.
            e.HasQueryFilter(t => t.Client == null || !t.Client.IsDeleted);
            e.Property(t => t.Subject).HasMaxLength(200).IsRequired();
            e.Property(t => t.Type).HasConversion<string>().HasMaxLength(30);
            e.Property(t => t.Priority).HasConversion<string>().HasMaxLength(15);
            e.Property(t => t.Status).HasConversion<string>().HasMaxLength(15);
            e.Property(t => t.Origin).HasConversion<string>().HasMaxLength(15);
            e.Property(t => t.Description).IsRequired();
            e.Property(t => t.SupportGroup).HasMaxLength(100).IsRequired(false);
            e.Property(t => t.ResolutionMessage).IsRequired(false);
            e.Property(t => t.RootCause).IsRequired(false);
            e.Property(t => t.SlaAlertSentAt).IsRequired(false);
            e.Property(t => t.SlaCompliant).IsRequired(false);
            e.HasOne(t => t.Client)
             .WithMany()
             .HasForeignKey(t => t.ClientId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.AssignedTo)
             .WithMany()
             .HasForeignKey(t => t.AssignedToUserId)
             .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.CreatedBy)
             .WithMany()
             .HasForeignKey(t => t.CreatedByUserId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── TicketNotification ────────────────────────────────────────────────
        mb.Entity<TicketNotification>(e =>
        {
            e.ToTable("TicketNotifications");
            e.HasKey(n => n.Id);
            e.Property(n => n.Type).HasConversion<string>().HasMaxLength(30);
            e.Property(n => n.Status).HasConversion<string>().HasMaxLength(10);
            e.Property(n => n.Recipient).HasMaxLength(200).IsRequired();
            e.Property(n => n.Message).IsRequired();
            e.Property(n => n.ErrorDetail).IsRequired(false);
            e.HasOne(n => n.Ticket)
             .WithMany(t => t.Notifications)
             .HasForeignKey(n => n.TicketId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── TicketComment ─────────────────────────────────────────────────────
        mb.Entity<TicketComment>(e =>
        {
            e.ToTable("TicketComments");
            e.HasKey(c => c.Id);
            e.Property(c => c.Type).HasConversion<string>().HasMaxLength(30);
            e.Property(c => c.Body).IsRequired();
            e.HasOne(c => c.Ticket)
             .WithMany(t => t.Comments)
             .HasForeignKey(c => c.TicketId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Author)
             .WithMany()
             .HasForeignKey(c => c.AuthorId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── TicketWorkLog ─────────────────────────────────────────────────────
        mb.Entity<TicketWorkLog>(e =>
        {
            e.ToTable("TicketWorkLogs");
            e.HasKey(w => w.Id);
            e.Property(w => w.Notes).IsRequired(false);
            e.HasOne(w => w.Ticket)
             .WithMany(t => t.WorkLogs)
             .HasForeignKey(w => w.TicketId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(w => w.User)
             .WithMany()
             .HasForeignKey(w => w.UserId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── TicketVisit ───────────────────────────────────────────────────────
        mb.Entity<TicketVisit>(e =>
        {
            e.ToTable("TicketVisits");
            e.HasKey(v => v.Id);
            e.Property(v => v.Observations).IsRequired(false);
            e.HasOne(v => v.Ticket)
             .WithMany(t => t.Visits)
             .HasForeignKey(v => v.TicketId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(v => v.Technician)
             .WithMany()
             .HasForeignKey(v => v.TechnicianId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── SlaPlan ───────────────────────────────────────────────────────────
        mb.Entity<SlaPlan>(e =>
        {
            e.ToTable("SlaPlans");
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.Priority).IsUnique();
            e.Property(s => s.Name).HasMaxLength(100).IsRequired();
            e.Property(s => s.Priority).HasMaxLength(15).IsRequired();
            e.Property(s => s.Schedule).HasConversion<string>().HasMaxLength(20);
        });

        mb.Entity<SlaPlan>().HasData(
            new SlaPlan { Id = Guid.Parse("00000000-0000-0000-0002-000000000001"), Name = "SLA Crítico", Priority = "Critica", FirstResponseMinutes = 15,  ResolutionMinutes = 240,  Schedule = SlaSchedule.Veinticuatro7, IsActive = true, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new SlaPlan { Id = Guid.Parse("00000000-0000-0000-0002-000000000002"), Name = "SLA Alto",    Priority = "Alta",    FirstResponseMinutes = 30,  ResolutionMinutes = 480,  Schedule = SlaSchedule.Veinticuatro7, IsActive = true, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new SlaPlan { Id = Guid.Parse("00000000-0000-0000-0002-000000000003"), Name = "SLA Medio",   Priority = "Media",   FirstResponseMinutes = 120, ResolutionMinutes = 1440, Schedule = SlaSchedule.Laboral,       IsActive = true, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new SlaPlan { Id = Guid.Parse("00000000-0000-0000-0002-000000000004"), Name = "SLA Bajo",    Priority = "Baja",    FirstResponseMinutes = 240, ResolutionMinutes = 4320, Schedule = SlaSchedule.Laboral,       IsActive = true, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        );

        // ── WhatsAppReceipt ───────────────────────────────────────────────────
        mb.Entity<WhatsAppReceipt>(e =>
        {
            e.ToTable("WhatsAppReceipts");
            e.HasKey(r => r.Id);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(r => r.Status);
            e.HasIndex(r => r.ReceivedAt);
            e.HasOne(r => r.Client)
             .WithMany()
             .HasForeignKey(r => r.ClientId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── DashboardPreference ───────────────────────────────────────────────
        mb.Entity<DashboardPreference>(e =>
        {
            e.ToTable("DashboardPreferences");
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.UserId).IsUnique();
            e.Property(p => p.ShowKpis).HasDefaultValue(true);
            e.Property(p => p.ShowTendencia).HasDefaultValue(true);
            e.Property(p => p.ShowTickets).HasDefaultValue(true);
            e.Property(p => p.ShowWhatsApp).HasDefaultValue(true);
            e.Property(p => p.ShowDeudores).HasDefaultValue(true);
            e.Property(p => p.ShowZonas).HasDefaultValue(true);
            e.Property(p => p.ShowMetodosPago).HasDefaultValue(true);
            e.Property(p => p.ShowComprobantes).HasDefaultValue(true);
            e.Property(p => p.UpdatedAt);
            e.HasOne<UserSystem>()
             .WithOne()
             .HasForeignKey<DashboardPreference>(p => p.UserId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── NotifConfig ───────────────────────────────────────────────────────
        mb.Entity<NotifConfig>(e =>
        {
            e.ToTable("NotifConfigs");
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.Tipo).IsUnique();
            e.Property(c => c.Tipo).HasConversion<string>().HasMaxLength(30);
            e.Property(c => c.HoraInicio).HasColumnType("time");
            e.Property(c => c.HoraFin).HasColumnType("time");
            e.Property(c => c.PlantillaId).IsRequired(false); // US-NOT-04
        });

        mb.Entity<NotifPlantilla>(e =>
        {
            e.ToTable("NotifPlantillas");
            e.HasKey(p => p.Id);
            e.HasIndex(p => new { p.Tipo, p.Activa });
            e.Property(p => p.Tipo).HasConversion<string>().HasMaxLength(30);
            e.Property(p => p.Texto).IsRequired();
            // US-NOT-03
            e.Property(p => p.Categoria).HasConversion<string>().HasMaxLength(20);
            e.Property(p => p.HsmStatus).HasConversion<string>().HasMaxLength(15);
        });

        // US-NOT-02: NotifSegments
        mb.Entity<PasswordResetToken>(e =>
        {
            e.ToTable("PasswordResetTokens");
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.Token).IsUnique();
            e.Property(t => t.Token).HasMaxLength(100);
            e.Property(t => t.Channel).HasMaxLength(20);
        });

        mb.Entity<NotifSegment>(e =>
        {
            e.ToTable("NotifSegments");
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.Nombre).IsUnique();
            e.Property(s => s.Nombre).HasMaxLength(100).IsRequired();
            e.Property(s => s.Descripcion).HasMaxLength(500);
            e.Property(s => s.ReglasJson).HasColumnType("jsonb").HasDefaultValue("[]");
        });

        mb.Entity<NotifPlantillaHistorial>(e =>
        {
            e.ToTable("NotifPlantillaHistorial");
            e.HasKey(h => h.Id);
            e.Property(h => h.Tipo).HasConversion<string>().HasMaxLength(30);
            e.Property(h => h.Texto).IsRequired();
        });

        mb.Entity<NotifOutbox>(e =>
        {
            e.ToTable("NotifOutbox");
            e.HasKey(o => o.Id);
            e.HasIndex(o => new { o.EstadoFinal, o.Publicado, o.EnviarDesde });
            e.Property(o => o.Tipo).HasConversion<string>().HasMaxLength(30);
            e.Property(o => o.EstadoFinal).HasConversion<string>().HasMaxLength(15).IsRequired(false);
            e.Property(o => o.PhoneNumber).HasMaxLength(30);
            e.Property(o => o.ContextoJson).HasColumnType("jsonb").HasDefaultValue("{}");
            e.HasOne(o => o.Cliente)
             .WithMany()
             .HasForeignKey(o => o.ClienteId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<NotifLog>(e =>
        {
            e.ToTable("NotifLogs");
            e.HasKey(l => l.Id);
            e.HasIndex(l => new { l.ClienteId, l.RegistradoAt });
            e.HasIndex(l => new { l.ClienteId, l.Tipo, l.RegistradoAt });
            e.Property(l => l.Tipo).HasConversion<string>().HasMaxLength(30);
            e.Property(l => l.Estado).HasConversion<string>().HasMaxLength(15);
            e.Property(l => l.PhoneNumber).HasMaxLength(30);
            e.Property(l => l.Mensaje).IsRequired();
            e.Property(l => l.ErrorDetalle).IsRequired(false);
            e.HasOne(l => l.Cliente)
             .WithMany()
             .HasForeignKey(l => l.ClienteId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Installation ──────────────────────────────────────────────────────
        mb.Entity<Installation>(e =>
        {
            e.ToTable("Installations");
            e.HasKey(i => i.Id);
            e.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(i => i.Direccion).HasMaxLength(300).IsRequired();
            e.Property(i => i.CanceladoPor).HasMaxLength(10).IsRequired(false);
            e.Property(i => i.HoraInicio).HasColumnType("time without time zone");
            e.HasIndex(i => new { i.Fecha, i.HoraInicio });
            e.HasIndex(i => i.Status);
            e.HasIndex(i => i.ClientId);
            e.HasOne(i => i.Client).WithMany().HasForeignKey(i => i.ClientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.Plan).WithMany().HasForeignKey(i => i.PlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.Tecnico).WithMany().HasForeignKey(i => i.TecnicoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(i => i.Ticket).WithMany().HasForeignKey(i => i.TicketId).OnDelete(DeleteBehavior.SetNull);
        });

        // ── ClientQr ──────────────────────────────────────────────────────────
        mb.Entity<ClientQr>(e =>
        {
            e.ToTable("ClientQrs");
            e.HasKey(q => q.Id);
            e.HasIndex(q => new { q.ClientId, q.IsActive });
            e.HasIndex(q => new { q.ExpiresAt, q.IsActive, q.AlertSent });
            e.HasOne(q => q.Client).WithMany().HasForeignKey(q => q.ClientId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── PlanChangeRequest ─────────────────────────────────────────────────
        mb.Entity<PlanChangeRequest>(e =>
        {
            e.ToTable("PlanChangeRequests");
            e.HasKey(r => r.Id);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(15);
            e.HasIndex(r => new { r.ClientId, r.Status });
            e.HasOne(r => r.Client).WithMany().HasForeignKey(r => r.ClientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.OldPlan).WithMany().HasForeignKey(r => r.OldPlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.NewPlan).WithMany().HasForeignKey(r => r.NewPlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.Ticket).WithMany().HasForeignKey(r => r.TicketId).OnDelete(DeleteBehavior.SetNull);
        });

        // BUG FIX: HasData eliminado — Program.cs ya maneja el seed con AnyAsync check
        // para evitar el doble insert del admin con el mismo Guid que causa excepción de PK duplicada.
        // HasData de EF Core genera una migración que inserta el registro incondicionalmente,
        // mientras Program.cs lo inserta solo si no existe. Ambos en paralelo = conflicto.

        mb.Entity<TbnSequence>().HasData(new TbnSequence { Id = 1, LastValue = 0, Prefix = "TBN" });

        mb.Entity<Plan>().HasData(
            new Plan { Id = Guid.Parse("00000000-0000-0000-0001-000000000001"), Name = "Plan Cobre", SpeedMb = 30, MonthlyPrice = 99.00m,  IsActive = true, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Plan { Id = Guid.Parse("00000000-0000-0000-0001-000000000002"), Name = "Plan Plata", SpeedMb = 50, MonthlyPrice = 149.00m, IsActive = true, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Plan { Id = Guid.Parse("00000000-0000-0000-0001-000000000003"), Name = "Plan Oro",   SpeedMb = 80, MonthlyPrice = 199.00m, IsActive = true, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        );

        var seedDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var adminId  = Guid.Parse("00000000-0000-0000-0000-000000000001");

        mb.Entity<NotifConfig>().HasData(
            new NotifConfig { Id = Guid.Parse("00000000-0000-0000-0008-000000000001"), Tipo = NotifType.SUSPENSION,        Activo = true, DelaySegundos = 0,    HoraInicio = new TimeOnly(8,0), HoraFin = new TimeOnly(20,0), Inmediato = false, DiasAntes = null, ActualizadoAt = seedDate },
            new NotifConfig { Id = Guid.Parse("00000000-0000-0000-0008-000000000002"), Tipo = NotifType.REACTIVACION,      Activo = true, DelaySegundos = 0,    HoraInicio = new TimeOnly(8,0), HoraFin = new TimeOnly(20,0), Inmediato = false, DiasAntes = null, ActualizadoAt = seedDate },
            new NotifConfig { Id = Guid.Parse("00000000-0000-0000-0008-000000000003"), Tipo = NotifType.RECORDATORIO_R1,   Activo = true, DelaySegundos = 0,    HoraInicio = new TimeOnly(8,0), HoraFin = new TimeOnly(20,0), Inmediato = false, DiasAntes = 5,   ActualizadoAt = seedDate },
            new NotifConfig { Id = Guid.Parse("00000000-0000-0000-0008-000000000004"), Tipo = NotifType.RECORDATORIO_R2,   Activo = true, DelaySegundos = 0,    HoraInicio = new TimeOnly(8,0), HoraFin = new TimeOnly(20,0), Inmediato = false, DiasAntes = 3,   ActualizadoAt = seedDate },
            new NotifConfig { Id = Guid.Parse("00000000-0000-0000-0008-000000000005"), Tipo = NotifType.RECORDATORIO_R3,   Activo = true, DelaySegundos = 0,    HoraInicio = new TimeOnly(8,0), HoraFin = new TimeOnly(20,0), Inmediato = false, DiasAntes = 1,   ActualizadoAt = seedDate },
            new NotifConfig { Id = Guid.Parse("00000000-0000-0000-0008-000000000006"), Tipo = NotifType.FACTURA_VENCIDA,   Activo = true, DelaySegundos = 3600, HoraInicio = new TimeOnly(8,0), HoraFin = new TimeOnly(20,0), Inmediato = false, DiasAntes = null, ActualizadoAt = seedDate },
            new NotifConfig { Id = Guid.Parse("00000000-0000-0000-0008-000000000007"), Tipo = NotifType.CONFIRMACION_PAGO, Activo = true, DelaySegundos = 0,    HoraInicio = new TimeOnly(8,0), HoraFin = new TimeOnly(20,0), Inmediato = true,  DiasAntes = null, ActualizadoAt = seedDate },
            // US-NOT-04: nuevos triggers
            new NotifConfig { Id = Guid.Parse("00000000-0000-0000-0008-000000000009"), Tipo = NotifType.TICKET_CREADO,    Activo = true, DelaySegundos = 0, HoraInicio = new TimeOnly(8,0), HoraFin = new TimeOnly(20,0), Inmediato = false, DiasAntes = null, ActualizadoAt = seedDate },
            new NotifConfig { Id = Guid.Parse("00000000-0000-0000-0008-000000000010"), Tipo = NotifType.TICKET_RESUELTO,  Activo = true, DelaySegundos = 0, HoraInicio = new TimeOnly(8,0), HoraFin = new TimeOnly(20,0), Inmediato = false, DiasAntes = null, ActualizadoAt = seedDate },
            new NotifConfig { Id = Guid.Parse("00000000-0000-0000-0008-000000000011"), Tipo = NotifType.CAMBIO_PLAN,      Activo = true, DelaySegundos = 0, HoraInicio = new TimeOnly(8,0), HoraFin = new TimeOnly(20,0), Inmediato = false, DiasAntes = null, ActualizadoAt = seedDate }
        );

        mb.Entity<NotifPlantilla>().HasData(
            new NotifPlantilla { Id = Guid.Parse("00000000-0000-0000-0009-000000000001"), Tipo = NotifType.SUSPENSION,        Activa = true, CreadoAt = seedDate, CreadoPorId = adminId, Texto = "Estimado/a {{nombre}},\n\nSu servicio *{{plan}}* ha sido *suspendido* por falta de pago.\nComuníquese con nosotros para regularizar su cuenta.\n\n*TelecomBoliviaNet*" },
            new NotifPlantilla { Id = Guid.Parse("00000000-0000-0000-0009-000000000002"), Tipo = NotifType.REACTIVACION,      Activa = true, CreadoAt = seedDate, CreadoPorId = adminId, Texto = "Estimado/a {{nombre}},\n\nSu servicio *{{plan}}* ha sido *reactivado* exitosamente. ¡Ya puede usarlo con normalidad!\n\n*TelecomBoliviaNet*" },
            new NotifPlantilla { Id = Guid.Parse("00000000-0000-0000-0009-000000000003"), Tipo = NotifType.RECORDATORIO_R1,   Activa = true, CreadoAt = seedDate, CreadoPorId = adminId, Texto = "Estimado/a {{nombre}},\n\nLe recordamos que tiene una factura de *Bs. {{monto}}* con vencimiento el *{{fecha_vencimiento}}* ({{meses_pendientes}} mes(es) pendiente(s)).\n\nEvite inconvenientes pagando antes del vencimiento.\n\n*TelecomBoliviaNet*" },
            new NotifPlantilla { Id = Guid.Parse("00000000-0000-0000-0009-000000000004"), Tipo = NotifType.RECORDATORIO_R2,   Activa = true, CreadoAt = seedDate, CreadoPorId = adminId, Texto = "Estimado/a {{nombre}},\n\n⚠️ Su factura de *Bs. {{monto}}* vence el *{{fecha_vencimiento}}*. Evite la suspensión del servicio pagando a tiempo.\n\n*TelecomBoliviaNet*" },
            new NotifPlantilla { Id = Guid.Parse("00000000-0000-0000-0009-000000000005"), Tipo = NotifType.RECORDATORIO_R3,   Activa = true, CreadoAt = seedDate, CreadoPorId = adminId, Texto = "Estimado/a {{nombre}},\n\n🚨 Su factura vence *mañana* ({{fecha_vencimiento}}). Monto: *Bs. {{monto}}*.\nRealice su pago hoy para no perder el servicio.\n\n*TelecomBoliviaNet*" },
            new NotifPlantilla { Id = Guid.Parse("00000000-0000-0000-0009-000000000006"), Tipo = NotifType.FACTURA_VENCIDA,   Activa = true, CreadoAt = seedDate, CreadoPorId = adminId, Texto = "Estimado/a {{nombre}},\n\nSu factura del periodo *{{periodo}}* por *Bs. {{monto}}* está *vencida*.\nPor favor regularice su pago para evitar la suspensión del servicio.\n\n*TelecomBoliviaNet*" },
            new NotifPlantilla { Id = Guid.Parse("00000000-0000-0000-0009-000000000007"), Tipo = NotifType.CONFIRMACION_PAGO, Activa = true, CreadoAt = seedDate, CreadoPorId = adminId, Texto = "Estimado/a {{nombre}},\n\n✅ Hemos registrado su pago de *Bs. {{monto}}* correspondiente al periodo *{{periodo}}*.\n\nGracias por su pago puntual. *TelecomBoliviaNet*" },
            // US-NOT-04: nuevas plantillas para triggers nuevos
            new NotifPlantilla { Id = Guid.Parse("00000000-0000-0000-0009-000000000009"), Tipo = NotifType.TICKET_CREADO,   Activa = true, Categoria = PlantillaCategoria.Ticket, HsmStatus = HsmStatus.Aprobada, CreadoAt = seedDate, CreadoPorId = adminId, Texto = "Estimado/a {{nombre}},\n\nSu solicitud de soporte ha sido registrada con el número *{{num_ticket}}*.\n\nLe atenderemos a la brevedad. *{{empresa}}*" },
            new NotifPlantilla { Id = Guid.Parse("00000000-0000-0000-0009-000000000010"), Tipo = NotifType.TICKET_RESUELTO, Activa = true, Categoria = PlantillaCategoria.Ticket, HsmStatus = HsmStatus.Aprobada, CreadoAt = seedDate, CreadoPorId = adminId, Texto = "Estimado/a {{nombre}},\n\nSu ticket *{{num_ticket}}* ha sido *resuelto* por {{tecnico}}.\n\nSi tiene alguna consulta adicional, contáctenos. *{{empresa}}*" },
            new NotifPlantilla { Id = Guid.Parse("00000000-0000-0000-0009-000000000011"), Tipo = NotifType.CAMBIO_PLAN,     Activa = true, Categoria = PlantillaCategoria.Cobro,  HsmStatus = HsmStatus.Aprobada, CreadoAt = seedDate, CreadoPorId = adminId, Texto = "Estimado/a {{nombre}},\n\nSu plan de servicio ha sido actualizado a *{{plan}}*.\n\nEl cambio es efectivo inmediatamente. *{{empresa}}*" }
        );

        // ── RefreshToken ──────────────────────────────────────────────────────
        mb.Entity<RefreshToken>(e =>
        {
            e.ToTable("RefreshTokens");
            e.HasKey(t => t.Id);
            e.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            e.Property(t => t.CreatedByIp).HasMaxLength(50);
            e.Property(t => t.ReplacedByTokenHash).HasMaxLength(64);
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => new { t.UserId, t.ExpiresAt });
        });

        // ── SystemConfig ──────────────────────────────────────────────────────
        mb.Entity<SystemConfig>(e =>
        {
            e.ToTable("SystemConfigs");
            e.HasKey(c => c.Id);
            e.Property(c => c.Key).HasMaxLength(100).IsRequired();
            e.Property(c => c.Value).IsRequired();
            e.Property(c => c.IsSecret).HasDefaultValue(false);
            e.HasIndex(c => c.Key).IsUnique();
        });
    }
}
