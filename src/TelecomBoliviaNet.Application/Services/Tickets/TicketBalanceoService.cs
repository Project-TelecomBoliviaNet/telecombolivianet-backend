using TelecomBoliviaNet.Application.DTOs.Tickets;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Services.Tickets;

/// <summary>
/// US-TKT-BALANCEO — Asignación automática de técnico por menor carga de trabajo.
/// Selecciona el técnico activo con menos tickets abiertos/en-proceso.
/// En empate, prioriza el que no ha tenido asignaciones recientes.
/// </summary>
public class TicketBalanceoService
{
    private readonly IGenericRepository<SupportTicket> _ticketRepo;
    private readonly IGenericRepository<UserSystem>    _userRepo;

    public TicketBalanceoService(
        IGenericRepository<SupportTicket> ticketRepo,
        IGenericRepository<UserSystem>    userRepo)
    {
        _ticketRepo = ticketRepo;
        _userRepo   = userRepo;
    }

    /// <summary>
    /// Devuelve el ID del técnico con menor carga de tickets activos.
    /// Retorna null si no hay técnicos disponibles.
    /// </summary>
    public async Task<Guid?> GetTecnicoMenorCargaAsync(string? preferredGroup = null)
    {
        // Obtener todos los técnicos activos
        var tecnicos = await _userRepo.GetAll()
            .Where(u => u.Role == UserRole.Tecnico && u.Status == UserStatus.Activo)
            .Select(u => u.Id)
            .ToListAsync();

        if (!tecnicos.Any()) return null;

        // Contar tickets activos (Abierto + EnProceso) por técnico
        var cargaPorTecnico = await _ticketRepo.GetAll()
            .Where(t =>
                t.AssignedToUserId.HasValue &&
                tecnicos.Contains(t.AssignedToUserId!.Value) &&
                (t.Status == TicketStatus.Abierto || t.Status == TicketStatus.EnProceso))
            .GroupBy(t => t.AssignedToUserId!.Value)
            .Select(g => new { TecnicoId = g.Key, Carga = g.Count() })
            .ToListAsync();

        // Técnicos sin tickets asignados tienen carga 0
        var cargaMap = cargaPorTecnico.ToDictionary(x => x.TecnicoId, x => x.Carga);
        foreach (var id in tecnicos)
            if (!cargaMap.ContainsKey(id)) cargaMap[id] = 0;

        // Ordenar por carga ascendente, en empate por último assignment
        var menorCarga = cargaMap.OrderBy(kv => kv.Value).First();

        // Si hay empate (varios con misma carga mínima), elegir el que no fue asignado más recientemente
        var minCarga  = menorCarga.Value;
        var candidatos = cargaMap.Where(kv => kv.Value == minCarga).Select(kv => kv.Key).ToList();

        if (candidatos.Count == 1) return candidatos[0];

        // Entre los candidatos con igual carga, el menos recientemente asignado
        var ultimaAsignacion = await _ticketRepo.GetAll()
            .Where(t => t.AssignedToUserId.HasValue && candidatos.Contains(t.AssignedToUserId!.Value))
            .GroupBy(t => t.AssignedToUserId!.Value)
            .Select(g => new { TecnicoId = g.Key, Ultimo = g.Max(t => t.CreatedAt) })
            .ToListAsync();

        var asignMap = ultimaAsignacion.ToDictionary(x => x.TecnicoId, x => x.Ultimo);
        foreach (var id in candidatos)
            if (!asignMap.ContainsKey(id)) asignMap[id] = DateTime.MinValue;

        return asignMap.OrderBy(kv => kv.Value).First().Key;
    }

    /// <summary>
    /// US-TKT-BALANCEO — Resumen de carga por técnico para el panel admin.
    /// </summary>
    public async Task<List<TecnicoCargaDto>> GetCargaResumenAsync()
    {
        var tecnicos = await _userRepo.GetAll()
            .Where(u => u.Role == UserRole.Tecnico && u.Status == UserStatus.Activo)
            .ToListAsync();

        var tecnicoIds = tecnicos.Select(u => u.Id).ToList();

        var activos = await _ticketRepo.GetAll()
            .Where(t => t.AssignedToUserId.HasValue
                     && tecnicoIds.Contains(t.AssignedToUserId!.Value)
                     && (t.Status == TicketStatus.Abierto || t.Status == TicketStatus.EnProceso))
            .GroupBy(t => t.AssignedToUserId!.Value)
            .Select(g => new
            {
                Id       = g.Key,
                Total    = g.Count(),
                Criticos = g.Count(t => t.Priority == TicketPriority.Critica)
            })
            .ToListAsync();

        var map = activos.ToDictionary(x => x.Id, x => x);

        return tecnicos
            .Select(t => new TecnicoCargaDto(
                t.Id, t.FullName,
                map.TryGetValue(t.Id, out var m) ? m.Total    : 0,
                map.TryGetValue(t.Id, out var n) ? n.Criticos : 0))
            .OrderBy(x => x.TicketsActivos)
            .ToList();
    }
}


