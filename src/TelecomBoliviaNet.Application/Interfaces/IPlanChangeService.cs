using TelecomBoliviaNet.Application.DTOs.Clients;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Interfaces;

public interface IPlanChangeService
{
    Task<List<PlanChangeItemDto>>      GetPendientesAsync(Guid? clientId = null);
    Task<List<PlanChangeHistorialDto>> GetHistorialAsync(Guid clientId);
    Task<Result<Guid>>                 SolicitarCambioAsync(
        Guid clientId, Guid newPlanId, string? notes, Guid actorId, string actorName, string ip);
    Task<Result<bool>>                 AprobarCambioAsync(
        Guid cambioId, bool midMonth, Guid actorId, string actorName, string ip);
    Task<Result<bool>>                 RechazarCambioAsync(
        Guid cambioId, string motivo, Guid actorId, string actorName, string ip);
}
