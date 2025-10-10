using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Installations;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Installations;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Installations.Queries;

public record GetInstalacionSlotsQuery(int DiasAdelante = 7)
    : IRequest<List<SlotDisponibleDto>>;

public class GetInstalacionSlotsHandler
    : IRequestHandler<GetInstalacionSlotsQuery, List<SlotDisponibleDto>>
{
    private readonly IGenericRepository<Installation> _instRepo;
    private readonly IGenericRepository<UserSystem>   _userRepo;
    private const int DuracionEstandarMin = 120;

    public GetInstalacionSlotsHandler(
        IGenericRepository<Installation> instRepo,
        IGenericRepository<UserSystem>   userRepo)
    {
        _instRepo = instRepo;
        _userRepo = userRepo;
    }

    public async Task<List<SlotDisponibleDto>> Handle(
        GetInstalacionSlotsQuery req, CancellationToken ct)
    {
        var hoy   = DateTime.UtcNow.Date;
        var hasta = hoy.AddDays(req.DiasAdelante);

        var instActivas = await _instRepo.GetAll()
            .Where(i =>
                i.Fecha >= hoy && i.Fecha <= hasta &&
                (i.Status == InstallationStatus.Pendiente ||
                 i.Status == InstallationStatus.EnProceso))
            .ToListAsync(ct);

        var tecnicosDisponibles = await CountTecnicosAsync(ct);

        var horariosOfrecidos = new[]
        {
            new TimeOnly(8,  0),
            new TimeOnly(10, 0),
            new TimeOnly(14, 0),
            new TimeOnly(16, 0),
        };

        var slots = new List<SlotDisponibleDto>();

        for (var dia = hoy; dia <= hasta; dia = dia.AddDays(1))
        {
            if (dia == hoy && DateTime.UtcNow.Hour >= 16) continue;
            if (dia.DayOfWeek == DayOfWeek.Sunday) continue;

            foreach (var hora in horariosOfrecidos)
            {
                var ocupados    = instActivas.Count(i => i.Fecha.Date == dia && i.HoraInicio == hora);
                var disponibles = tecnicosDisponibles - ocupados;

                slots.Add(new SlotDisponibleDto(
                    Fecha:       dia.ToString("yyyy-MM-dd"),
                    HoraInicio:  hora.ToString("HH:mm"),
                    HoraFin:     hora.AddMinutes(DuracionEstandarMin).ToString("HH:mm"),
                    Disponibles: Math.Max(0, disponibles),
                    Disponible:  disponibles > 0
                ));
            }
        }

        return slots;
    }

    private async Task<int> CountTecnicosAsync(CancellationToken ct)
    {
        var count = await _userRepo.GetAll()
            .CountAsync(u => u.Role == UserRole.Tecnico && u.Status == UserStatus.Activo, ct);
        return Math.Max(count, 1);
    }
}
