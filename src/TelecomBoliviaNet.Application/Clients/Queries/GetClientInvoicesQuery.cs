using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.Clients.Helpers;
using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Clients.Queries;

public record GetClientInvoicesQuery(Guid ClientId, int Year) : IRequest<InvoiceGridDto>;

public class GetClientInvoicesHandler : IRequestHandler<GetClientInvoicesQuery, InvoiceGridDto>
{
    private readonly IGenericRepository<Invoice> _invoiceRepo;
    private readonly IGenericRepository<Payment> _paymentRepo;

    public GetClientInvoicesHandler(
        IGenericRepository<Invoice> invoiceRepo,
        IGenericRepository<Payment> paymentRepo)
    {
        _invoiceRepo = invoiceRepo;
        _paymentRepo = paymentRepo;
    }

    public async Task<InvoiceGridDto> Handle(GetClientInvoicesQuery q, CancellationToken ct)
    {
        var invoices = await _invoiceRepo.GetAll()
            .Where(i => i.ClientId == q.ClientId &&
                        (i.Year == q.Year || i.Type == InvoiceType.Instalacion) &&
                        i.Status != InvoiceStatus.Anulada)
            .OrderBy(i => i.Month)
            .ToListAsync(ct);

        var payments = await _paymentRepo.GetAll()
            .Include(p => p.RegisteredBy)
            .Include(p => p.PaymentInvoices)
                .ThenInclude(pi => pi.Invoice)
            .Where(p => p.ClientId == q.ClientId)
            .OrderByDescending(p => p.PaidAt)
            .ToListAsync(ct);

        var debt    = ClientProjectionHelpers.CalcDebt(
            invoices.Where(i => i.Year == q.Year || i.Type == InvoiceType.Instalacion));
        var pending = ClientProjectionHelpers.CalcPendingMonths(invoices);
        var lastPay = payments.Select(p => (DateTime?)p.PaidAt).FirstOrDefault();

        return new InvoiceGridDto(
            invoices.Select(ClientProjectionHelpers.MapInvoice),
            payments.Select(ClientProjectionHelpers.MapPaymentDto),
            debt, pending, lastPay);
    }
}
