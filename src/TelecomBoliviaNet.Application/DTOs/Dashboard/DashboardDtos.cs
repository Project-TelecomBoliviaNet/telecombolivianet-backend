namespace TelecomBoliviaNet.Application.DTOs.Dashboard;

// ── KPIs completos ────────────────────────────────────────────────────────────
public record DashboardKpisDto(
    // Clientes
    int     ClientesActivos,
    int     ClientesSuspendidos,
    int     ClientesNuevosMes,
    // Cobros
    decimal CobradoEsteMes,
    decimal CobradoMesAnterior,
    int     ClientesConDeuda,
    decimal MontoDeudaTotal,
    // Tickets
    int     TicketsAbiertos,
    int     TicketsCriticos,
    int     TicketsResueltosHoy,
    int     TicketsSlaVencidos,
    // WhatsApp
    int     ComprobantesPendientes,
    int     MensajesWspHoy
);

// ── Tendencia cobros ──────────────────────────────────────────────────────────
public record TendenciaMesDto(string Mes, string MesCompleto, decimal Total, int Cantidad);
public record TendenciaCobrosDto(List<TendenciaMesDto> Meses);

// ── Métodos de pago ───────────────────────────────────────────────────────────
public record MetodoPagoDto(string Metodo, int Cantidad, decimal Monto);

// ── Tickets por estado ────────────────────────────────────────────────────────
public record TicketEstadoDashDto(string Estado, int Total, int Criticos);

// ── Ticket reciente para dashboard ───────────────────────────────────────────
public record TicketDashItemDto(
    Guid     Id,
    string   ClienteNombre,
    string   Asunto,
    string   Tipo,
    string   Prioridad,
    string   Estado,
    string   Tecnico,
    DateTime CreadoEn,
    DateTime? FechaLimite,
    bool     SlaVencido
);

// ── Ticket por tipo (dona) ────────────────────────────────────────────────────
public record TicketPorTipoDto(string Tipo, int Total);

// ── Tiempo promedio resolución por prioridad ──────────────────────────────────
public record ResolucionPromDto(string Prioridad, double HorasPromedio, int Cantidad);

// ── WhatsApp actividad ────────────────────────────────────────────────────────
public record WhatsAppActividadDto(
    string   Hora,
    string   ClienteNombre,
    string   Estado,
    decimal? Monto
);

// ── Deudores ──────────────────────────────────────────────────────────────────
public record DeudorItemDto(
    Guid    ClienteId,
    string  ClienteNombre,
    string  Zona,
    decimal MontoDeuda,
    int     DiasVencido,
    int     FacturasVencidas
);

// ── Comprobantes recientes ────────────────────────────────────────────────────
public record ComprobanteRecienteDto(
    Guid     Id,
    string   ClienteNombre,
    decimal  Monto,
    DateTime FechaPago,
    string   Estado,
    string   Metodo
);

// ── Clientes por zona ─────────────────────────────────────────────────────────
public record ClientesPorZonaDto(string Zona, int Total, int Activos, int ConDeuda);

// ── Actividad por hora ────────────────────────────────────────────────────────
public record ActividadHoraDto(int Hora, int Pagos, int Tickets, int WhatsApp);

// ── Preferencias ──────────────────────────────────────────────────────────────
public record DashboardPreferencesDto(
    bool ShowKpis,
    bool ShowTendencia,
    bool ShowTickets,
    bool ShowWhatsApp,
    bool ShowDeudores,
    bool ShowZonas,
    bool ShowMetodosPago,
    bool ShowComprobantes
);

// ════════════════════════════════════════════════════════════════════════════
// M4: US-DASH-PAGOS · Sección cobros enriquecida
// ════════════════════════════════════════════════════════════════════════════

public record DashPagosDto(
    decimal TotalHoy,
    decimal TotalMes,
    int     CountHoy,
    int     CountMes,
    List<DashPagosPorMetodoDto>   PorMetodo,
    List<DashPagosPorOperadorDto> PorOperador,
    List<TendenciaMesDto>         Tendencia6M
);

public record DashPagosPorMetodoDto(
    string  Metodo,
    int     Cantidad,
    decimal Monto,
    decimal PctMonto   // porcentaje del total del mes
);

public record DashPagosPorOperadorDto(
    string  OperadorNombre,
    int     Cantidad,
    decimal Monto
);

// ════════════════════════════════════════════════════════════════════════════
// M4: US-DASH-TICKETS-M · Métricas de tickets
// ════════════════════════════════════════════════════════════════════════════

public record DashTicketsMetricasDto(
    int     TotalAbiertos,
    int     TotalEnProceso,
    int     TotalResueltosMes,
    int     TotalVencidosSla,       // tickets con SLA vencido aún abiertos
    double  SlaCompliancePct,       // % resueltos dentro del SLA
    double  ResolucionPromedioHoras,
    List<DashTicketPorTipoMetricaDto> PorTipo,
    List<TicketDashItemDto>           VencidosSla  // top 5 más críticos
);

public record DashTicketPorTipoMetricaDto(
    string Tipo,
    int    Total,
    int    Abiertos,
    int    Resueltos,
    double PromedioHoras
);

// ════════════════════════════════════════════════════════════════════════════
// M4: US-DASH-NOTIF · Estado de notificaciones
// ════════════════════════════════════════════════════════════════════════════

public record DashNotifDto(
    int     EnviadasUlt24h,
    int     FallidasUlt24h,
    int     PendientesEnCola,
    int     OmitidosAntispam,
    double  TasaExitoUlt24h,     // % sobre (enviadas + fallidas)
    List<DashNotifPorTipoDto> PorTipo
);

public record DashNotifPorTipoDto(
    string NotifTipo,
    int    Enviadas,
    int    Fallidas,
    int    Pendientes
);

// ════════════════════════════════════════════════════════════════════════════
// M4: US-DASH-CHATBOT · Métricas del chatbot
// ════════════════════════════════════════════════════════════════════════════

public record DashChatbotDto(
    int    ConversacionesActivas,
    int    ConversacionesHoy,
    int    ConversacionesMes,
    double TasaResolucionBot,      // % sin escalar a humano
    double TasaEscaladoHumano,
    List<DashChatbotIntencionDto> IntencionesFrecuentes
);

public record DashChatbotIntencionDto(
    string Intencion,
    int    Ocurrencias,
    double PctTotal
);

// ════════════════════════════════════════════════════════════════════════════
// M4: US-DASH-AUTO · Acciones automáticas del día
// ════════════════════════════════════════════════════════════════════════════

public record DashAutoActionsDto(
    int     SuspensionesHoy,
    int     ReactivacionesHoy,
    int     FacturasEmitidasHoy,
    int     RecordatoriosEnviadosHoy,
    int     FacturasVencidasMarcadasHoy,
    List<DashAutoActionItemDto> Recientes  // últimas 10 acciones automáticas
);

public record DashAutoActionItemDto(
    string   Accion,
    string   Detalle,
    DateTime OcurridoAt,
    string   AreaModulo   // Facturación | Notificaciones | Clientes
);

// ════════════════════════════════════════════════════════════════════════════
// M4: US-DASH-DRILL · Drill-down por KPI
// ════════════════════════════════════════════════════════════════════════════

public record DashDrillDownDto(
    string       Kpi,        // cobros | deuda | tickets | clientes
    string       Periodo,
    object       Data        // payload específico por KPI
);
