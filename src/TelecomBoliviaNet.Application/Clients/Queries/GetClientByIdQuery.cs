using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.Clients.Helpers;
using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Application.DTOs.Plans;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Clients.Queries;

public record GetClientByIdQuery(Guid ClientId) : IRequest<ClientDetailDto?>;

public class GetClientByIdHandler : IRequestHandler<GetClientByIdQuery, ClientDetailDto?>
{
    private readonly IGenericRepository<Client> _clientRepo;

    public GetClientByIdHandler(IGenericRepository<Client> clientRepo) => _clientRepo = clientRepo;

    public async Task<ClientDetailDto?> Handle(GetClientByIdQuery q, CancellationToken ct)
    {
        var c = await _clientRepo.GetAll()
            .IgnoreQueryFilters()
            .Include(c => c.Plan)
            .Include(c => c.InstalledBy)
            .Include(c => c.Invoices)
                .ThenInclude(i => i.PaymentInvoices)
                    .ThenInclude(pi => pi.Payment)
            .FirstOrDefaultAsync(c => c.Id == q.ClientId, ct);

        if (c is null) return null;

        var debt    = ClientProjectionHelpers.CalcDebt(c.Invoices);
        var pending = ClientProjectionHelpers.CalcPendingMonths(c.Invoices);
        var lastPay = c.Invoices
            .SelectMany(i => i.PaymentInvoices)
            .Select(pi => pi.Payment?.PaidAt)
            .Where(d => d.HasValue)
            .OrderByDescending(d => d)
            .FirstOrDefault();
        var instPaid = c.Invoices
            .FirstOrDefault(i => i.Type == InvoiceType.Instalacion)?.Status == InvoiceStatus.Pagada;

        return new ClientDetailDto(
            c.Id, c.TbnCode, c.FullName, c.IdentityCard,
            c.PhoneMain, c.PhoneSecondary,
            c.Zone, c.Street, c.LocationRef, c.GpsLatitude, c.GpsLongitude,
            c.WinboxNumber, c.InstallationDate,
            c.InstalledByUserId, c.InstalledBy?.FullName ?? "—",
            c.Plan is null ? null! : new PlanDto(c.Plan.Id, c.Plan.Name, c.Plan.SpeedMb,
                                                  c.Plan.MonthlyPrice, c.Plan.IsActive, c.Plan.DisplayLabel),
            c.HasTvCable, c.OnuSerialNumber,
            c.Status.ToString(), c.SuspendedAt, c.CancelledAt,
            debt, pending, lastPay, instPaid, c.CreatedAt,
            c.Email,
            c.CreditBalance,
            0,
            c.Xmin);
    }
}
