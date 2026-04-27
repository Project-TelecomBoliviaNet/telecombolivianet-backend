// ═══════════════════════════════════════════════════════════════════════════════
// ARCHIVO: ClientService.BotExtensions.cs
//
// Extensión parcial de ClientService con el método GetByPhoneAsync.
// Agregar este archivo a la carpeta Services/Clients del proyecto Application.
//
// INSTRUCCIÓN DE INTEGRACIÓN:
//   Opción A (recomendada): Copiar el cuerpo del método GetByPhoneAsync
//   directamente dentro de la clase ClientService existente en ClientService.cs,
//   antes del bloque de Helpers.
//
//   Opción B: Convertir ClientService en clase partial y usar este archivo
//   como segunda parte (requiere agregar "partial" a la declaración original).
//
// Se documenta aquí como archivo independiente para claridad del diff.
// ═══════════════════════════════════════════════════════════════════════════════

using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Interfaces;
namespace TelecomBoliviaNet.Application.Services.Clients;

/// <summary>
/// Método a añadir en ClientService.cs — búsqueda de cliente por teléfono para el bot.
/// </summary>
public static class ClientServiceBotExtensions
{
    // ── Bot: buscar cliente por número de teléfono ────────────────────────────
    //
    // Normaliza el número antes de buscar:
    //   - Quita el prefijo internacional 591 si viene del bot
    //   - Busca tanto en PhoneMain como en PhoneSecondary
    //
    // Llamado desde: GET /api/clients/by-phone?phone={phone}
    // Consumidor:    chatbot NestJS — SistemaApiService.getClientByPhone()
    //
    public static async Task<ClientBotDto?> GetByPhoneAsync(
        IGenericRepository<Client> clientRepo,
        string rawPhone)
    {
        // Normalizar: quitar el prefijo 591 si viene con él
        var phone = rawPhone.Trim();
        if (phone.StartsWith("591") && phone.Length > 3)
            phone = phone[3..];

        var client = await clientRepo.GetAll()
            .Include(c => c.Plan)
            .Include(c => c.Invoices)
            .FirstOrDefaultAsync(c =>
                c.PhoneMain      == phone      ||
                c.PhoneMain      == rawPhone   ||
                c.PhoneSecondary == phone      ||
                c.PhoneSecondary == rawPhone);

        if (client is null) return null;

        var pendingInvoices = client.Invoices
            .Where(i => i.Status is InvoiceStatus.Pendiente or InvoiceStatus.Vencida)
            .ToList();

        return new ClientBotDto(
            Id:           client.Id.ToString(),
            TbnCode:      client.TbnCode,
            FullName:     client.FullName,
            PhoneMain:    client.PhoneMain,
            Status:       client.Status.ToString(),
            PlanId:       client.PlanId.ToString(),
            PlanName:     client.Plan?.Name ?? "—",
            PlanSpeedMbps: client.Plan?.SpeedMb ?? 0,
            TotalDebt:    pendingInvoices.Sum(i => i.Amount),
            PendingMonths: pendingInvoices.Count(i => i.Type == InvoiceType.Mensualidad),
            Zone:         client.Zone
        );
    }
}
