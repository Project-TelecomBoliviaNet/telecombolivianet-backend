using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Common;
using TelecomBoliviaNet.Application.DTOs.Invoices;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Services.Invoices;

public class InvoiceQueryService
{
    private readonly IGenericRepository<Invoice>        _invoiceRepo;
    private readonly IGenericRepository<Client>         _clientRepo;
    private readonly IGenericRepository<PaymentInvoice> _piRepo;

    private static readonly string[] MonthNames =
        ["", "Ene", "Feb", "Mar", "Abr", "May", "Jun",
             "Jul", "Ago", "Sep", "Oct", "Nov", "Dic"];

    public InvoiceQueryService(
        IGenericRepository<Invoice>        invoiceRepo,
        IGenericRepository<Client>         clientRepo,
        IGenericRepository<PaymentInvoice> piRepo)
    {
        _invoiceRepo = invoiceRepo;
        _clientRepo  = clientRepo;
        _piRepo      = piRepo;
    }

    // ── US-23 · Estadísticas del mes ─────────────────────────────────────────

    public async Task<InvoiceMonthStatsDto> GetMonthStatsAsync(int year, int month)
    {
        var invoices = await _invoiceRepo.GetAll()
            .Where(i => i.Year == year && i.Month == month && i.Type == InvoiceType.Mensualidad)
            .ToListAsync();

        var billed    = invoices.Sum(i => i.Amount);
        var collected = invoices.Where(i => i.Status == InvoiceStatus.Pagada).Sum(i => i.Amount);
        var pending   = invoices.Where(i => i.Status is InvoiceStatus.Pendiente or InvoiceStatus.Vencida)
                                .Sum(i => i.Amount);
        var rate      = billed > 0 ? Math.Round(collected / billed * 100, 1) : 0;

        return new InvoiceMonthStatsDto(
            year, month, billed, collected, pending,
            invoices.Count,
            invoices.Count(i => i.Status == InvoiceStatus.Pagada),
            invoices.Count(i => i.Status == InvoiceStatus.Pendiente),
            invoices.Count(i => i.Status == InvoiceStatus.Vencida),
            rate);
    }

    // ── US-23 · Listado paginado con filtros ──────────────────────────────────

    public async Task<PagedResult<InvoiceDetailDto>> GetPagedAsync(InvoiceFilterDto filter)
    {
        var query = _invoiceRepo.GetAll()
            .Include(i => i.Client).ThenInclude(c => c!.Plan)
            .Include(i => i.PaymentInvoices).ThenInclude(pi => pi.Payment)
                .ThenInclude(p => p!.RegisteredBy)
            .Where(i => i.Type == InvoiceType.Mensualidad);

        // Filtro por mes/año solo si se especifican (búsqueda global sin mes es válida)
        if (filter.Year.HasValue)
            query = query.Where(i => i.Year == filter.Year.Value);
        if (filter.Month.HasValue)
            query = query.Where(i => i.Month == filter.Month.Value);

        if (!string.IsNullOrWhiteSpace(filter.Status) && filter.Status != "all")
        {
            if (Enum.TryParse<InvoiceStatus>(filter.Status, out var status))
                query = query.Where(i => i.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var q = filter.Search.ToLower();
            query = query.Where(i =>
                (i.Client != null && (i.Client.FullName.ToLower().Contains(q) ||
                                     i.Client.TbnCode.ToLower().Contains(q))));
        }

        query = query.OrderBy(i => i.Client!.TbnCode);

        var total    = await query.CountAsync();
        var invoices = await query
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync();

        return new PagedResult<InvoiceDetailDto>(
            invoices.Select(MapToDetail),
            total, filter.PageNumber, filter.PageSize);
    }

    // ── US-27 · Reporte anual ─────────────────────────────────────────────────

    public async Task<IEnumerable<AnnualReportRowDto>> GetAnnualReportAsync(AnnualReportFilterDto filter)
    {
        // Traer todos los clientes
        var clientsQuery = _clientRepo.GetAll()
            .Include(c => c.Plan)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Zone))
            clientsQuery = clientsQuery.Where(c => c.Zone.ToLower().Contains(filter.Zone.ToLower()));

        if (filter.PlanId.HasValue)
            clientsQuery = clientsQuery.Where(c => c.PlanId == filter.PlanId.Value);

        clientsQuery = filter.SortBy == "name"
            ? clientsQuery.OrderBy(c => c.FullName)
            : clientsQuery.OrderBy(c => c.TbnCode);

        var clients = await clientsQuery.ToListAsync();

        // Traer todas las facturas del año y de instalación
        var clientIds = clients.Select(c => c.Id).ToList();
        var invoices  = await _invoiceRepo.GetAll()
            .Include(i => i.PaymentInvoices).ThenInclude(pi => pi.Payment)
            .Where(i => clientIds.Contains(i.ClientId) &&
                        (i.Year == filter.Year || i.Type == InvoiceType.Instalacion))
            .ToListAsync();

        var rows = new List<AnnualReportRowDto>();

        foreach (var client in clients)
        {
            var clientInvoices = invoices.Where(i => i.ClientId == client.Id).ToList();

            // Calcular deuda total para el filtro de deuda
            var debt = clientInvoices
                .Where(i => i.Status is InvoiceStatus.Pendiente or InvoiceStatus.Vencida)
                .Sum(i => i.Amount);

            if (filter.DebtFilter == "paid"  && debt > 0) continue;
            if (filter.DebtFilter == "debt"  && debt == 0) continue;

            var cells = new List<AnnualReportCellDto>();

            // Celda de instalación (month=0)
            var instInvoice = clientInvoices.FirstOrDefault(i => i.Type == InvoiceType.Instalacion);
            cells.Add(BuildCell(0, "Instalación", instInvoice));

            // 12 meses del año
            for (int m = 1; m <= 12; m++)
            {
                var monthInvoice = clientInvoices
                    .FirstOrDefault(i => i.Year == filter.Year && i.Month == m
                                        && i.Type == InvoiceType.Mensualidad);

                // "NoAplica" si el cliente se instaló después de ese mes
                if (monthInvoice is null)
                {
                    var installDate = client.InstallationDate;
                    var isBeforeInstall = new DateTime(filter.Year, m, 1) < new DateTime(installDate.Year, installDate.Month, 1);
                    cells.Add(new AnnualReportCellDto(m, MonthNames[m],
                        isBeforeInstall ? "NoAplica" : "NoGenerada", 0, null, null, null));
                }
                else
                {
                    cells.Add(BuildCell(m, MonthNames[m], monthInvoice));
                }
            }

            rows.Add(new AnnualReportRowDto(
                client.Id, client.TbnCode, client.FullName,
                client.Zone, client.Plan?.Name ?? "—", cells));
        }

        return rows;
    }

    // ── US-24 · Listado completo para exportación (sin paginado) ────────────

    public async Task<IEnumerable<InvoiceLegacyListItemDto>> GetAllForExportAsync(int year, int month)
    {
        var invoices = await _invoiceRepo.GetAll()
            .Include(i => i.Client).ThenInclude(c => c!.Plan)
            .Include(i => i.PaymentInvoices)
            .Where(i => i.Year == year && i.Month == month && i.Type == InvoiceType.Mensualidad)
            .OrderBy(i => i.Client!.TbnCode)
            .ToListAsync();

        return invoices.Select(i => new InvoiceLegacyListItemDto(
                i.Id, i.Client!.TbnCode    ?? "—",
            i.Client?.FullName   ?? "—",
            i.Client?.Plan?.Name ?? "—",
            i.Amount,
            i.Type.ToString(),
            i.Status.ToString(),
            i.Year, i.Month,
            i.IssuedAt, i.DueDate,
            i.Notes,
            i.PaymentInvoices.FirstOrDefault()?.PaymentId));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AnnualReportCellDto BuildCell(int month, string label, Invoice? invoice)
    {
        if (invoice is null)
            return new AnnualReportCellDto(month, label, "NoAplica", 0, null, null, null);

        var payment = invoice.PaymentInvoices
            .Select(pi => pi.Payment)
            .FirstOrDefault(p => p is not null);

        return new AnnualReportCellDto(
            month, label, invoice.Status.ToString(), invoice.Amount,
            payment?.Method.ToString(), payment?.PaidAt, invoice.Id);
    }

    private static InvoiceDetailDto MapToDetail(Invoice i)
    {
        var payment = i.PaymentInvoices
            .Select(pi => pi.Payment)
            .FirstOrDefault(p => p is not null);

        return new InvoiceDetailDto(
            i.Id,
            i.ClientId,
            i.Client?.TbnCode    ?? "—",
            i.Client?.FullName   ?? "—",
            i.Client?.Plan?.Name ?? "—",
            i.Type.ToString(),
            i.Status.ToString(),
            i.Year, i.Month, i.Amount,
            i.IssuedAt, i.DueDate, i.Notes, i.UpdatedAt,
            payment?.Method.ToString(),
            payment?.Bank,
            payment?.PaidAt,
            payment?.RegisteredBy?.FullName);
    }
}
