using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Domain.Entities.Tickets;

/// <summary>US-TKT-TIPOS: tipos extendidos de ticket.</summary>
public enum TicketType
{
    SoporteTecnico, InstalacionNueva, CambioPlan,
    TvCable, ReactivacionServicio, BajaServicio, RecoleccionEquipo
}

public enum TicketPriority { Critica, Alta, Media, Baja }

public enum TicketStatus { Abierto, EnProceso, Resuelto, Cerrado }

public enum TicketOrigin { Bot, Manual, Automatico }

/// <summary>Ticket de soporte — Módulo 7, US-01 a US-21.</summary>
public class SupportTicket : Entity
{
    public Guid           ClientId          { get; set; }
    public Client?        Client            { get; set; }

    // US-01 campos obligatorios
    public string         Subject           { get; set; } = string.Empty;
    public TicketType     Type              { get; set; }
    public TicketPriority Priority          { get; set; }
    public TicketStatus   Status            { get; set; } = TicketStatus.Abierto;
    public TicketOrigin   Origin            { get; set; }
    public string         Description       { get; set; } = string.Empty;
    public string?        SupportGroup      { get; set; }

    // Asignación
    public Guid?          AssignedToUserId  { get; set; }
    public UserSystem?    AssignedTo        { get; set; }
    public Guid           CreatedByUserId   { get; set; }
    public UserSystem?    CreatedBy         { get; set; }

    // Fechas
    public DateTime       CreatedAt         { get; set; } = DateTime.UtcNow;
    public DateTime?      DueDate           { get; set; }
    public DateTime?      FirstRespondedAt  { get; set; }   // US-08
    public DateTime?      ResolvedAt        { get; set; }
    public DateTime?      ClosedAt          { get; set; }   // US-19/US-20

    // Resolución (US-15, US-16)
    public string?        ResolutionMessage { get; set; }
    public string?        RootCause         { get; set; }

    // CSAT (US-18)
    public int?           CsatScore         { get; set; }
    public DateTime?      CsatRespondedAt   { get; set; }

    // SLA
    public DateTime?      SlaAlertSentAt    { get; set; }
    public bool?          SlaCompliant      { get; set; }

    // Navegación
    // US-TKT-CORRELATIVO: número correlativo TK-AAAA-NNNN
    public string?    TicketNumber     { get; set; }

    // US-TKT-SLA: deadline calculado al asignar SLA
    public DateTime?  SlaDeadline      { get; set; }

    // US-TKT-BALANCEO: flag que indica asignación automática por carga
    public bool       AutoAssigned     { get; set; } = false;

    public ICollection<TicketComment>      Comments { get; set; } = new List<TicketComment>();
    public ICollection<TicketWorkLog>      WorkLogs { get; set; } = new List<TicketWorkLog>();
    public ICollection<TicketVisit>        Visits   { get; set; } = new List<TicketVisit>();
    public ICollection<TicketNotification> Notifications { get; set; } = new List<TicketNotification>();
}
