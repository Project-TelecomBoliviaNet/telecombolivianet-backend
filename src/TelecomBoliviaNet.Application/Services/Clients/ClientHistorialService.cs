using TelecomBoliviaNet.Domain.Entities.Audit;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Domain.Entities.Admin;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Services.Clients;

/// <summary>
/// US-CLI-HISTORIAL — Historial cronológico de toda la actividad del cliente:
/// pagos, facturas, tickets, cambios de estado, notificaciones WhatsApp, acciones admin.
/// </summary>
public class ClientHistorialService
{
    private readonly IGenericRepository<Payment>         _paymentRepo;
    private readonly IGenericRepository<Invoice>         _invoiceRepo;
    private readonly IGenericRepository<SupportTicket>   _ticketRepo;
    private readonly IGenericRepository<AuditLog>        _auditRepo;
    private readonly IGenericRepository<NotifLog>        _notifRepo;
    private readonly IGenericRepository<ClientAttachment> _attachRepo;

    public ClientHistorialService(
        IGenericRepository<Payment>          paymentRepo,
        IGenericRepository<Invoice>          invoiceRepo,
        IGenericRepository<SupportTicket>    ticketRepo,
        IGenericRepository<AuditLog>         auditRepo,
        IGenericRepository<NotifLog>         notifRepo,
        IGenericRepository<ClientAttachment> attachRepo)
    {
        _paymentRepo = paymentRepo;
        _invoiceRepo = invoiceRepo;
        _ticketRepo  = ticketRepo;
        _auditRepo   = auditRepo;
        _notifRepo   = notifRepo;
        _attachRepo  = attachRepo;
    }

    public async Task<ClientHistorialDto> GetHistorialAsync(
        Guid clientId, int page, int pageSize,
        string? tipoFilter, DateTime? desde, DateTime? hasta)
    {
        var items = new List<ClientActivityItemDto>();

        // ── Pagos ──────────────────────────────────────────────────────────────
        if (tipoFilter is null or "Pago")
        {
            var pagos = await _paymentRepo.GetAll()
                .Include(p => p.RegisteredBy)
                .Where(p => p.ClientId == clientId && !p.IsVoided
                && (!desde.HasValue || p.PaidAt >= desde.Value)
                && (!hasta.HasValue || p.PaidAt <= hasta.Value))
                .ToListAsync();

            items.AddRange(pagos.Select(p => new ClientActivityItemDto(
                p.Id, "Pago",
                $"Pago de Bs. {p.Amount:F2} ({p.Method})",
                p.RegisteredBy?.FullName ?? "Sistema",
                p.PaidAt,
                p.Id.ToString(), null)));
        }

        // ── Facturas ───────────────────────────────────────────────────────────
        if (tipoFilter is null or "Factura")
        {
            var facturas = await _invoiceRepo.GetAll()
                .Where(i => i.ClientId == clientId
                && (!desde.HasValue || i.IssuedAt >= desde.Value)
                && (!hasta.HasValue || i.IssuedAt <= hasta.Value))
                .ToListAsync();

            items.AddRange(facturas.Select(i => new ClientActivityItemDto(
                i.Id, "Factura",
                $"Factura {i.InvoiceNumber ?? i.Id.ToString()[..8]} — {i.Status} Bs. {i.Amount:F2}",
                "Sistema",
                i.IssuedAt,
                i.Id.ToString(), null)));
        }

        // ── Tickets ────────────────────────────────────────────────────────────
        if (tipoFilter is null or "Ticket")
        {
            var tickets = await _ticketRepo.GetAll()
                .Include(t => t.CreatedBy)
                .Where(t => t.ClientId == clientId
                && (!desde.HasValue || t.CreatedAt >= desde.Value)
                && (!hasta.HasValue || t.CreatedAt <= hasta.Value))
                .ToListAsync();

            items.AddRange(tickets.Select(t => new ClientActivityItemDto(
                t.Id, "Ticket",
                $"Ticket {t.TicketNumber ?? t.Id.ToString()[..8]} — {t.Status} ({t.Priority}): {(t.Description?.Length > 60 ? t.Description[..60] + "…" : t.Description)}",
                t.CreatedBy?.FullName ?? "Sistema",
                t.CreatedAt,
                t.Id.ToString(), null)));
        }

        // ── Notificaciones WhatsApp ────────────────────────────────────────────
        if (tipoFilter is null or "Notif")
        {
            var notifs = await _notifRepo.GetAll()
                .Where(n => n.ClienteId == clientId
                && (!desde.HasValue || n.RegistradoAt >= desde.Value)
                && (!hasta.HasValue || n.RegistradoAt <= hasta.Value))
                .ToListAsync();

            items.AddRange(notifs.Select(n => new ClientActivityItemDto(
                n.Id, "Notif",
                $"WhatsApp {n.Tipo}: {n.Estado}",
                "Sistema",
                n.RegistradoAt,
                n.OutboxId.ToString(), null)));
        }

        // ── Acciones Admin (audit log relevante) ──────────────────────────────
        if (tipoFilter is null or "Admin" or "Estado")
        {
            // BUG FIX: Filtrar por EntityId indexado en lugar de Description.Contains
            // para evitar falsos positivos y mejorar rendimiento.
            var auditItems = await _auditRepo.GetAll()
                .Where(a => a.EntityId == clientId
                && (!desde.HasValue || a.CreatedAt >= desde.Value)
                && (!hasta.HasValue || a.CreatedAt <= hasta.Value))
                .ToListAsync();

            items.AddRange(auditItems.Select(a => new ClientActivityItemDto(
                a.Id, a.Action.StartsWith("CLIENT_STATUS") ? "Estado" : "Admin",
                a.Description != string.Empty ? a.Description : a.Action,
                a.UserName ?? "Sistema",
                a.CreatedAt,
                a.Id.ToString(), a.NewData)));
        }

        // ── Adjuntos ───────────────────────────────────────────────────────────
        if (tipoFilter is null or "Adjunto")
        {
            var adjuntos = await _attachRepo.GetAll()
                .Include(a => a.SubidoPor)
                .Where(a => a.ClientId == clientId
                && (!desde.HasValue || a.SubidoAt >= desde.Value)
                && (!hasta.HasValue || a.SubidoAt <= hasta.Value))
                .ToListAsync();

            items.AddRange(adjuntos.Select(a => new ClientActivityItemDto(
                a.Id, "Adjunto",
                $"Documento adjunto: {a.FileName} ({a.TipoDoc})",
                a.SubidoPor?.FullName ?? "Sistema",
                a.SubidoAt,
                a.Id.ToString(), null)));
        }

        // BUG FIX: filtros de fecha movidos a cada query SQL (ver arriba).
        // Eliminados los filtros post-carga en memoria que cargaban todos los registros
        // antes de filtrar, causando queries lentas y alto consumo de memoria.

        // ── Ordenar y paginar ─────────────────────────────────────────────────
        var total   = items.Count;
        var paged   = items
            .OrderByDescending(i => i.OcurridoAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new ClientHistorialDto(paged, total, page, pageSize);
    }
}
