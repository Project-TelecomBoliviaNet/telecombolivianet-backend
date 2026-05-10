using TelecomBoliviaNet.Application.DTOs.Installations;
using TelecomBoliviaNet.Domain.Entities.Installations;

namespace TelecomBoliviaNet.Application.Installations;

internal static class InstallationMapper
{
    internal static InstalacionDetalleDto ToDetalle(Installation i) => new(
        Id:                i.Id,
        ClienteTbn:        i.Client?.TbnCode   ?? "—",
        ClienteNombre:     i.Client?.FullName  ?? "—",
        ClientePhone:      i.Client?.PhoneMain ?? "—",
        PlanNombre:        i.Plan?.Name        ?? "—",
        Fecha:             i.Fecha.ToString("yyyy-MM-dd"),
        HoraInicio:        i.HoraInicio.ToString("HH:mm"),
        HoraFin:           i.HoraInicio.AddMinutes(i.DuracionMin).ToString("HH:mm"),
        DuracionMin:       i.DuracionMin,
        Direccion:         i.Direccion,
        Notas:             i.Notas,
        Status:            i.Status.ToString(),
        TecnicoNombre:     i.Tecnico?.FullName,
        TecnicoId:         i.TecnicoId,
        TicketId:          i.TicketId,
        MotivoCancelacion: i.MotivoCancelacion,
        CanceladoPor:      i.CanceladoPor,
        CreadoAt:          i.CreadoAt
    );

    internal static InstalacionListItemDto ToListItem(Installation i) => new(
        Id:            i.Id,
        ClienteTbn:    i.Client?.TbnCode  ?? "—",
        ClienteNombre: i.Client?.FullName ?? "—",
        PlanNombre:    i.Plan?.Name       ?? "—",
        Fecha:         i.Fecha.ToString("yyyy-MM-dd"),
        HoraInicio:    i.HoraInicio.ToString("HH:mm"),
        Status:        i.Status.ToString(),
        TecnicoNombre: i.Tecnico?.FullName,
        TicketId:      i.TicketId,
        CreadoAt:      i.CreadoAt
    );
}
