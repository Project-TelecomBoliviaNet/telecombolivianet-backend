using TelecomBoliviaNet.Application.DTOs.Dashboard;

namespace TelecomBoliviaNet.Application.Interfaces;

public interface IDashboardService
{
    Task<DashboardKpisDto>             GetKpisAsync();
    Task<TendenciaCobrosDto>           GetTendenciaCobrosAsync(int meses = 6);
    Task<List<MetodoPagoDto>>          GetMetodosPagoAsync();
    Task<List<TicketEstadoDashDto>>    GetTicketsEstadoAsync();
    Task<List<TicketDashItemDto>>      GetTicketsUrgentesAsync(int top = 8);
    Task<List<TicketPorTipoDto>>       GetTicketsPorTipoAsync();
    Task<List<ResolucionPromDto>>      GetResolucionPromedioAsync();
    Task<List<WhatsAppActividadDto>>   GetWhatsAppActividadAsync(int top = 10);
    Task<List<DeudorItemDto>>          GetDeudoresAsync();
    Task<List<ComprobanteRecienteDto>> GetComprobantesRecientesAsync(int top = 8);
    Task<List<ClientesPorZonaDto>>     GetClientesPorZonaAsync();
    Task<List<ActividadHoraDto>>       GetActividadHorasAsync();
    Task<DashboardPreferencesDto>      GetPreferencesAsync(Guid userId);
    Task                               SavePreferencesAsync(Guid userId, DashboardPreferencesDto prefs);
    // M4
    Task<DashPagosDto>                 GetDashPagosAsync();
    Task<DashTicketsMetricasDto>       GetDashTicketsMetricasAsync();
    Task<DashNotifDto>                 GetDashNotifAsync();
    Task<DashChatbotDto>               GetDashChatbotAsync();
    Task<DashAutoActionsDto>           GetDashAutoActionsAsync();
}
