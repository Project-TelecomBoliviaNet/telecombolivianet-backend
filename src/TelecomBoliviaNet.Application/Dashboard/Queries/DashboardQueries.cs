using MediatR;
using TelecomBoliviaNet.Application.DTOs.Dashboard;

namespace TelecomBoliviaNet.Application.Dashboard.Queries;

// ── KPIs / lecturas básicas ────────────────────────────────────────────────
public record GetDashKpisQuery                                  : IRequest<DashboardKpisDto>;
public record GetTendenciaCobrosQuery(int Meses = 6)            : IRequest<TendenciaCobrosDto>;
public record GetMetodosPagoQuery                               : IRequest<List<MetodoPagoDto>>;
public record GetTicketsEstadoQuery                             : IRequest<List<TicketEstadoDashDto>>;
public record GetTicketsUrgentesQuery(int Top = 8)              : IRequest<List<TicketDashItemDto>>;
public record GetTicketsPorTipoQuery                            : IRequest<List<TicketPorTipoDto>>;
public record GetResolucionPromedioQuery                        : IRequest<List<ResolucionPromDto>>;
public record GetWhatsAppActividadQuery(int Top = 10)           : IRequest<List<WhatsAppActividadDto>>;
public record GetDeudoresQuery                                  : IRequest<List<DeudorItemDto>>;
public record GetComprobantesRecientesQuery(int Top = 8)        : IRequest<List<ComprobanteRecienteDto>>;
public record GetClientesPorZonaQuery                          : IRequest<List<ClientesPorZonaDto>>;
public record GetActividadHorasQuery                           : IRequest<List<ActividadHoraDto>>;

// ── Preferencias ─────────────────────────────────────────────────────────
public record GetDashPreferencesQuery(Guid UserId)             : IRequest<DashboardPreferencesDto>;
public record SaveDashPreferencesCommand(Guid UserId, DashboardPreferencesDto Dto) : IRequest;

// ── M4 — secciones enriquecidas ───────────────────────────────────────────
public record GetDashPagosQuery                                : IRequest<DashPagosDto>;
public record GetDashTicketsMetricasQuery                      : IRequest<DashTicketsMetricasDto>;
public record GetDashNotifQuery                                : IRequest<DashNotifDto>;
public record GetDashChatbotQuery                              : IRequest<DashChatbotDto>;
public record GetDashAutoActionsQuery                          : IRequest<DashAutoActionsDto>;

// ── F5, F6, F7 — mejoras dashboard ────────────────────────────────────────
public record GetAgingCarteraQuery                             : IRequest<AgingCarteraDto>;
public record GetProximosCortQuery(int DiasVista = 7)          : IRequest<ProximosCortDto>;
public record GetWorkloadTecnicosQuery                         : IRequest<WorkloadTecnicosDto>;
