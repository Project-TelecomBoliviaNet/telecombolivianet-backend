using System.Globalization;
using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Domain.Entities.Clients;

namespace TelecomBoliviaNet.Application.Clients.Helpers;

internal static class ClientProjectionHelpers
{
    private static readonly CultureInfo _es = CultureInfo.GetCultureInfo("es-BO");
    internal static IQueryable<Client> ApplyFullTextSearch(IQueryable<Client> query, string searchTerm)
    {
        var q = searchTerm.ToLower();
        return query.Where(c =>
            c.FullName.ToLower().Contains(q)     ||
            c.TbnCode.ToLower().Contains(q)      ||
            c.IdentityCard.ToLower().Contains(q) ||
            c.WinboxNumber.ToLower().Contains(q) ||
            c.PhoneMain.Contains(q)              ||
            c.Zone.ToLower().Contains(q)         ||
            (c.PhoneSecondary != null && c.PhoneSecondary.Contains(q)) ||
            (c.Email          != null && c.Email.ToLower().Contains(q)));
    }

    internal static decimal CalcDebt(IEnumerable<Invoice> invoices) =>
        invoices.Where(i => i.IsUnpaid).Sum(i => i.Amount);

    internal static int CalcPendingMonths(IEnumerable<Invoice> invoices) =>
        invoices.Count(i => i.Type == InvoiceType.Mensualidad && i.IsUnpaid);

    internal static InvoiceDto MapInvoice(Invoice i) => new(
        i.Id, i.Type.ToString(), i.Status.ToString(),
        i.Year, i.Month, i.Amount, i.IssuedAt, i.DueDate, i.Notes);

    internal static PaymentDto MapPaymentDto(Payment p) => new(
        p.Id, p.Amount, p.Method.ToString(), p.Bank, p.BankReference, p.PhysicalReceiptNumber,
        p.PaidAt, p.RegisteredAt,
        p.RegisteredBy?.FullName ?? "Sistema",
        p.FromWhatsApp,
        p.ReceiptImageUrl,
        CanVoid:  !p.IsVoided && (DateTime.UtcNow - p.RegisteredAt).TotalDays <= 30,
        IsVoided: p.IsVoided,
        p.PaymentInvoices.Select(pi =>
        {
            if (pi.Invoice?.Type == InvoiceType.Instalacion) return "Instalación";
            if (pi.Invoice is null) return "—";
            var s = new DateTime(pi.Invoice.Year, pi.Invoice.Month, 1).ToString("MMMM yyyy", _es);
            return char.ToUpper(s[0]) + s[1..];
        }));
}
