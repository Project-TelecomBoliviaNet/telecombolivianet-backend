using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Clients.Queries;

public record GetClientByPhoneQuery(string Phone) : IRequest<ClientBotDto?>;

public class GetClientByPhoneHandler : IRequestHandler<GetClientByPhoneQuery, ClientBotDto?>
{
    private readonly IGenericRepository<Client> _clientRepo;

    public GetClientByPhoneHandler(IGenericRepository<Client> clientRepo) => _clientRepo = clientRepo;

    public async Task<ClientBotDto?> Handle(GetClientByPhoneQuery q, CancellationToken ct)
    {
        var rawPhone = q.Phone;
        var phone    = rawPhone.Trim();
        if (phone.StartsWith("591") && phone.Length > 3)
            phone = phone[3..];

        var client = await _clientRepo.GetAll()
            .Include(c => c.Plan)
            .Include(c => c.Invoices)
            .FirstOrDefaultAsync(c =>
                c.PhoneMain      == phone    ||
                c.PhoneMain      == rawPhone ||
                c.PhoneSecondary == phone    ||
                c.PhoneSecondary == rawPhone, ct);

        if (client is null) return null;

        var pendingInvoices = client.Invoices
            .Where(i => i.Status is InvoiceStatus.Pendiente or InvoiceStatus.Vencida)
            .ToList();

        return new ClientBotDto(
            Id:            client.Id.ToString(),
            TbnCode:       client.TbnCode,
            FullName:      client.FullName,
            PhoneMain:     client.PhoneMain,
            Status:        client.Status.ToString(),
            PlanId:        client.PlanId.ToString(),
            PlanName:      client.Plan?.Name ?? "—",
            PlanSpeedMbps: client.Plan?.SpeedMb ?? 0,
            TotalDebt:     pendingInvoices.Sum(i => i.Amount),
            PendingMonths: pendingInvoices.Count(i => i.Type == InvoiceType.Mensualidad),
            Zone:          client.Zone);
    }
}
