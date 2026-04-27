using TelecomBoliviaNet.Infrastructure.Security;
using TelecomBoliviaNet.Application.Services.Payments;
using TelecomBoliviaNet.Domain.Entities.Payments;
using TelecomBoliviaNet.Application.Services.Invoices;
using TelecomBoliviaNet.Infrastructure.Jobs;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Domain.Entities.Admin;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Infrastructure.Services;
using TelecomBoliviaNet.Infrastructure.Services.Clients;
using TelecomBoliviaNet.Application.Services.Bot;
using TelecomBoliviaNet.Presentation.Hubs;
using Microsoft.EntityFrameworkCore;
using FluentValidation;

// Auth (Módulo 1)
using TelecomBoliviaNet.Application.DTOs.Auth;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Application.Validators.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Audit;

// Clients / Plans (Módulo 2)
using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Application.DTOs.Plans;
using TelecomBoliviaNet.Application.Services.Clients;
using TelecomBoliviaNet.Application.Services.Plans;
using TelecomBoliviaNet.Application.Validators.Clients;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Plans;

// Módulo 7 — Tickets
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Application.Services.Tickets;
using TelecomBoliviaNet.Application.Validators.Tickets;

// Módulo 8a — Dashboard
using TelecomBoliviaNet.Infrastructure.Services.Dashboard;

// Módulo 8b — Notificaciones WhatsApp
using TelecomBoliviaNet.Application.Services.Notifications;
using TelecomBoliviaNet.Domain.Entities.Notifications;

// Módulo Admin Settings
using TelecomBoliviaNet.Application.Services.Admin;

// Módulo Instalaciones
using TelecomBoliviaNet.Application.Services.Installations;
using TelecomBoliviaNet.Domain.Entities.Installations;

// Infraestructura
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Infrastructure.Data;
using TelecomBoliviaNet.Infrastructure.Repositories;

namespace TelecomBoliviaNet.Presentation.Configuration;

public static class DependencyInjection
{
    // ── Punto de entrada único llamado desde Program.cs ───────────────────────
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services, IConfiguration config)
    {
        return services
            .AddDatabase(config)
            .AddRepositories()
            .AddServices()
            .AddValidators();
    }

    // ── Base de datos ─────────────────────────────────────────────────────────
    private static IServiceCollection AddDatabase(
        this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                config.GetConnectionString("DefaultConnection"),
                npgsql => npgsql.MigrationsAssembly(
                    "TelecomBoliviaNet.Infrastructure")));
        return services;
    }

    // ── Repositorios genéricos ────────────────────────────────────────────────
    private static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        // Módulo 1 — Auth
        services.AddScoped<IGenericRepository<UserSystem>,     GenericRepository<UserSystem>>();
        services.AddScoped<IGenericRepository<TokenBlacklist>, GenericRepository<TokenBlacklist>>();
        services.AddScoped<IGenericRepository<RefreshToken>,   GenericRepository<RefreshToken>>();
        services.AddScoped<IGenericRepository<AuditLog>,       GenericRepository<AuditLog>>();

        // Módulo 2 — Clientes y Planes
        services.AddScoped<IGenericRepository<Plan>,           GenericRepository<Plan>>();
        services.AddScoped<IGenericRepository<Client>,         GenericRepository<Client>>();
        services.AddScoped<IGenericRepository<Invoice>,        GenericRepository<Invoice>>();
        services.AddScoped<IGenericRepository<Payment>,        GenericRepository<Payment>>();
        services.AddScoped<IGenericRepository<PaymentInvoice>, GenericRepository<PaymentInvoice>>();

        // Módulo 7 — Tickets (extendido)
        services.AddScoped<IGenericRepository<SupportTicket>,      GenericRepository<SupportTicket>>();
        services.AddScoped<IGenericRepository<TicketNotification>, GenericRepository<TicketNotification>>();
        services.AddScoped<IGenericRepository<TicketComment>,      GenericRepository<TicketComment>>();
        services.AddScoped<IGenericRepository<TicketWorkLog>,      GenericRepository<TicketWorkLog>>();
        services.AddScoped<IGenericRepository<TicketVisit>,        GenericRepository<TicketVisit>>();
        services.AddScoped<IGenericRepository<SlaPlan>,            GenericRepository<SlaPlan>>();
        services.AddScoped<IGenericRepository<WhatsAppReceipt>,    GenericRepository<WhatsAppReceipt>>();

        // Módulo 8b — Notificaciones WhatsApp
        services.AddScoped<IGenericRepository<NotifConfig>,             GenericRepository<NotifConfig>>();
        services.AddScoped<IGenericRepository<NotifPlantilla>,          GenericRepository<NotifPlantilla>>();
        services.AddScoped<IGenericRepository<NotifPlantillaHistorial>, GenericRepository<NotifPlantillaHistorial>>();
        services.AddScoped<IGenericRepository<NotifOutbox>,             GenericRepository<NotifOutbox>>();
        services.AddScoped<IGenericRepository<NotifLog>,                GenericRepository<NotifLog>>();
        services.AddScoped<IGenericRepository<NotifSegment>,             GenericRepository<NotifSegment>>();  // US-NOT-02

        // Módulo Instalaciones
        services.AddScoped<IGenericRepository<Installation>,      GenericRepository<Installation>>();

        // Módulo QR
        services.AddScoped<IGenericRepository<ClientQr>,          GenericRepository<ClientQr>>();

        // Módulo Cambio de Plan
        services.AddScoped<IGenericRepository<PlanChangeRequest>,  GenericRepository<PlanChangeRequest>>();

        // Módulo Admin Settings (BD)
        services.AddScoped<IGenericRepository<SystemConfig>,       GenericRepository<SystemConfig>>();

        return services;
    }

    // ── Servicios de aplicación ───────────────────────────────────────────────
    private static IServiceCollection AddServices(this IServiceCollection services)
    {
        // CORRECCIÓN (Fix #11): Registrar IUnitOfWork para transacciones atómicas
        services.AddScoped<IUnitOfWork,        UnitOfWork>();
        services.AddScoped<IPasswordHasher,    BcryptPasswordHasher>();  // CORRECCIÓN #9
        services.AddScoped<ISequenceGenerator, SequenceGenerator>();      // CORRECCIÓN #3
        services.AddScoped<JwtTokenService>();
        services.AddScoped<AuditService>();
        services.AddScoped<RefreshTokenService>();
        services.AddScoped<AuthService>();
        services.AddScoped<UserSystemService>();
        services.AddScoped<AuditLogService>();

        // Módulo 2 — Clientes y Planes
        services.AddScoped<ITbnService, TbnService>();
        services.AddScoped<PlanService>();
        services.AddScoped<ClientService>();
        // IWhatsAppNotifier: sigue registrado por si algún servicio futuro lo necesita para envíos
        // directos fuera del outbox (ej: mensajes ad-hoc administrativos sin ventana horaria).
        // TicketService ya migró a INotifPublisher. Si no hay más consumidores directos,
        // este AddHttpClient puede eliminarse en la Fase 3.
        // BUG FIX: WhatsAppNotifier ya NO almacena el token en el constructor.
        // Lee el token dinámicamente desde AdminSettingsService en cada SendTextAsync
        // para reflejar cambios de token hechos desde el panel admin en runtime.
        // (fix aplicado en WhatsAppNotifier.cs — constructor + SendTextAsync)
        services.AddHttpClient<IWhatsAppNotifier, WhatsAppNotifier>();

        // Módulo 3 — Facturación
        services.AddScoped<BillingService>();
        services.AddScoped<InvoiceService>();
        // FIX CS0266: IBillingJob solo lo implementa InvoiceService — BillingService NO tiene la interfaz.
        services.AddScoped<IBillingJob>(sp => sp.GetRequiredService<InvoiceService>());
        services.AddScoped<InvoiceQueryService>();
        services.AddHostedService<BillingBackgroundJob>();
        // M2/M3 nuevos
        services.AddScoped<InvoiceNumberService>();          // US-FAC-CORRELATIVO
        services.AddScoped<IInvoiceNumberService>(sp => sp.GetRequiredService<InvoiceNumberService>());
        services.AddScoped<InvoiceM3Service>();              // US-FAC-02/ESTADOS/CREDITO                                                      // US-FAC-CORRELATIVO

        // Módulo 4 — Pagos
        services.AddScoped<PaymentService>();
        // BUG FIX: registro único — la interfaz delega a la misma instancia concreta del scope.
        // Evita dos instancias distintas por request que divergen en estado.
        services.AddScoped<PaymentCreditService>();
        services.AddScoped<IPaymentCreditService>(sp => sp.GetRequiredService<PaymentCreditService>());
        // M5
        // BUG FIX: registro único con factory
        services.AddScoped<ClientAttachmentService>();
        services.AddScoped<IClientAttachmentService>(sp => sp.GetRequiredService<ClientAttachmentService>());
        // M7
        services.AddScoped<IGenericRepository<PasswordResetToken>, GenericRepository<PasswordResetToken>>();
        // M9
        services.AddScoped<TicketNumberService>();
        // M10
        // BUG FIX: registro único con factory
        services.AddScoped<BotProxyService>();
        services.AddScoped<IBotProxyService>(sp => sp.GetRequiredService<BotProxyService>());
        // BUG FIX: registro único con factory
        services.AddScoped<BotConfigService>();
        services.AddScoped<IBotConfigService>(sp => sp.GetRequiredService<BotConfigService>());
        services.AddHttpClient("ChatbotMonitor")
            .ConfigureHttpClient((sp, client) =>
            {
                var cfg = sp.GetRequiredService<IConfiguration>();
                client.BaseAddress = new Uri(cfg["Chatbot:BaseUrl"] ?? "http://chatbot:3001");
                client.Timeout     = TimeSpan.FromSeconds(10);
            });
        services.AddScoped<TicketBalanceoService>();
        services.AddScoped<TicketAttachmentService>();
        services.AddScoped<IGenericRepository<TicketAttachment>, GenericRepository<TicketAttachment>>();
        services.AddScoped<ClientHistorialService>();
        services.AddScoped<IGenericRepository<ClientAttachment>, GenericRepository<ClientAttachment>>();                                                      // US-PAG-CREDITO/CAJA/RECIBO/06
        services.AddScoped<IGenericRepository<CashClose>,       GenericRepository<CashClose>>();        // US-PAG-CAJA
        services.AddScoped<IGenericRepository<PaymentReceipt>,  GenericRepository<PaymentReceipt>>();   // US-PAG-RECIBO
        services.AddScoped<WhatsAppReceiptService>();
        services.AddScoped<IFileStorage, LocalFileStorage>();
        services.AddScoped<IExportService, ExportService>();

        // Módulo 7 — Tickets (US-01 a US-21)
        services.AddScoped<TicketService>();
        services.AddHostedService<SlaAlertJob>();
        services.AddHostedService<AutoCloseTicketJob>();

        // Módulo 8a — Dashboard (US-D09 · US-D10 · US-D11 · US-D12)
        services.AddScoped<IDashboardService, DashboardService>();

        // Módulo 8b — Notificaciones WhatsApp
        // AdminHub notifier — desacopla Infrastructure de Presentation
        services.AddScoped<IAdminHubNotifier, AdminHubNotifier>();

        // INotifPublisher inserta en NotifOutbox. El Worker Python consume y envía WhatsApp.
        services.AddScoped<INotifPublisher, NotifPublisher>();
        // CORRECCIÓN #6: NotifConfigService dividido en servicios SRP
        services.AddScoped<NotifTriggerService>();
        services.AddScoped<NotifPlantillaService>();
        services.AddScoped<NotifSegmentService>();
        services.AddScoped<NotifEnvioService>();
        services.AddScoped<NotifHistorialService>();
        // Backward-compat facade — el controller sigue funcionando sin cambios de routing
        services.AddScoped<NotifConfigService>();

        // Módulo Admin Settings
        services.AddScoped<AdminSettingsService>();
        // BUG FIX: SecretEncryptionService registrado como Singleton para fail-fast en startup.
        // Su constructor lanza InvalidOperationException si SecretEncryption:Key no está configurada,
        // lo que explota al arrancar (no en el primer request) facilitando el diagnóstico.
        services.AddSingleton<SecretEncryptionService>();

        // Módulo Instalaciones
        services.AddScoped<InstallationService>();

        // Módulo QR — incluye job diario de alertas de vencimiento
        services.AddScoped<ClientQrService>();
        services.AddHostedService<QrExpiryAlertJob>();

        // Módulo Cambio de Plan
        services.AddScoped<PlanChangeService>();

        return services;
    }

    // ── Validadores FluentValidation ──────────────────────────────────────────
    private static IServiceCollection AddValidators(this IServiceCollection services)
    {
        // Módulo 1
        services.AddScoped<IValidator<LoginDto>,          LoginValidator>();
        services.AddScoped<IValidator<CreateUserDto>,     CreateUserValidator>();
        services.AddScoped<IValidator<UpdateUserDto>,     UpdateUserValidator>();
        services.AddScoped<IValidator<ChangePasswordDto>, ChangePasswordValidator>();

        // Módulo 2
        services.AddScoped<IValidator<RegisterClientDto>,  RegisterClientValidator>();
        services.AddScoped<IValidator<UpdateClientDto>,    UpdateClientValidator>();
        services.AddScoped<IValidator<RechazarCambioDto>, RechazarCambioValidator>();  // BUG FIX
        services.AddScoped<IValidator<RegisterPaymentDto>, RegisterPaymentValidator>();
        services.AddScoped<IValidator<CreatePlanDto>,      CreatePlanValidator>();
        services.AddScoped<IValidator<UpdatePlanDto>,      UpdatePlanValidator>();

        // Módulo 7 — Tickets (extendido)
        services.AddScoped<IValidator<CreateTicketDto>,       CreateTicketValidator>();
        services.AddScoped<IValidator<UpdateTicketDto>,       UpdateTicketValidator>();
        services.AddScoped<IValidator<ChangeTicketStatusDto>, ChangeTicketStatusValidator>();
        services.AddScoped<IValidator<AssignTicketDto>,       AssignTicketValidator>();
        services.AddScoped<IValidator<AddCommentDto>,         AddCommentValidator>();
        services.AddScoped<IValidator<AddWorkLogDto>,         AddWorkLogValidator>();
        services.AddScoped<IValidator<ScheduleVisitDto>,      ScheduleVisitValidator>();

        return services;
    }
}
