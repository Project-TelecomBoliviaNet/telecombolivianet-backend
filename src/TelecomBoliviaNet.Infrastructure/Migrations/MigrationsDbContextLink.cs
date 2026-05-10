using Microsoft.EntityFrameworkCore.Infrastructure;
using TelecomBoliviaNet.Infrastructure.Data;

namespace TelecomBoliviaNet.Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
partial class InitialCreate { }

[DbContext(typeof(AppDbContext))]
partial class AddClientsModule { }

[DbContext(typeof(AppDbContext))]
partial class AddSupportTickets { }

[DbContext(typeof(AppDbContext))]
partial class AddPaymentsModule { }

[DbContext(typeof(AppDbContext))]
partial class AddDashboardPreferences { }

[DbContext(typeof(AppDbContext))]
partial class AddTicketFullModule { }

[DbContext(typeof(AppDbContext))]
partial class ExpandDashboardPreferences { }

[DbContext(typeof(AppDbContext))]
partial class FixTicketFullModule { }

[DbContext(typeof(AppDbContext))]
partial class AddNotificationsModule { }

[DbContext(typeof(AppDbContext))]
partial class EnsureTicketColumnsExist { }

[DbContext(typeof(AppDbContext))]
partial class AddInstallationsModule { }

[DbContext(typeof(AppDbContext))]
partial class AddClientQr { }

[DbContext(typeof(AppDbContext))]
partial class AddPlanChangeRequest { }

[DbContext(typeof(AppDbContext))]
partial class AddPerformanceIndexesAndPhase1Fixes { }

[DbContext(typeof(AppDbContext))]
partial class AddRefreshTokens { }

[DbContext(typeof(AppDbContext))]
partial class AddSystemConfig { }

[DbContext(typeof(AppDbContext))]
partial class AddSoftDelete { }

[DbContext(typeof(AppDbContext))]
partial class AddNotifM1Features { }

[DbContext(typeof(AppDbContext))]
partial class AddM2PaymentsAndBilling { }

[DbContext(typeof(AppDbContext))]
partial class AddM5ClientFeatures { }

[DbContext(typeof(AppDbContext))]
partial class AddM7UsersAndRoles { }

[DbContext(typeof(AppDbContext))]
partial class AddM9TicketFeatures { }

[DbContext(typeof(AppDbContext))]
partial class AddPostgresSequences { }

[DbContext(typeof(AppDbContext))]
partial class AddNotifJobLogAndFixOutboxId { }

[DbContext(typeof(AppDbContext))]
partial class AddMetaTemplateFieldsToNotifPlantillas { }

[DbContext(typeof(AppDbContext))]
partial class SeedMetaTemplateConfig { }

[DbContext(typeof(AppDbContext))]
partial class DisableR2R3Reminders { }

[DbContext(typeof(AppDbContext))]
partial class AddInvoicePlanSnapshot { }

[DbContext(typeof(AppDbContext))]
partial class AddCambioPrecioNotif { }

[DbContext(typeof(AppDbContext))]
partial class AddSlaClienteWait { }

[DbContext(typeof(AppDbContext))]
partial class AddTicketAsignadoConfig { }

[DbContext(typeof(AppDbContext))]
partial class DisableR1R2Reminders { }

[DbContext(typeof(AppDbContext))]
partial class AddCompanyQrExpiryAlert { }

[DbContext(typeof(AppDbContext))]
partial class AddBusinessSchedule { }

[DbContext(typeof(AppDbContext))]
partial class AddAuditLogHardening { }

[DbContext(typeof(AppDbContext))]
partial class AddWhatsAppReceiptPhoneNumber { }

[DbContext(typeof(AppDbContext))]
partial class AddTicketImageUrl { }

[DbContext(typeof(AppDbContext))]
partial class AddMissingIndexes { }

[DbContext(typeof(AppDbContext))]
partial class AddPartialIndexes { }

[DbContext(typeof(AppDbContext))]
partial class AddAlertaSlaNotifType { }

[DbContext(typeof(AppDbContext))]
partial class AddPlanHistory { }

[DbContext(typeof(AppDbContext))]
partial class AddProspectFieldsToTickets { }

[DbContext(typeof(AppDbContext))]
partial class Phase2Improvements { }

[DbContext(typeof(AppDbContext))]
partial class Phase3Improvements { }

[DbContext(typeof(AppDbContext))]
partial class AddInternalAlerts { }

[DbContext(typeof(AppDbContext))]
partial class FixPlantillaCategoriaInvalidValues { }
