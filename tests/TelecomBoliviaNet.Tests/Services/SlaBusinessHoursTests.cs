using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TelecomBoliviaNet.Application.DTOs.Tickets;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Application.Services.Auth;
using TelecomBoliviaNet.Application.Tickets.Commands;
using TelecomBoliviaNet.Application.Tickets.Helpers;
using TelecomBoliviaNet.Domain.Entities.Audit;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Entities.Tickets;
using TelecomBoliviaNet.Domain.Interfaces;
using TelecomBoliviaNet.Tests.Helpers;
using Xunit;

namespace TelecomBoliviaNet.Tests.Services;

/// <summary>
/// Pruebas exhaustivas del cálculo de horario laboral SLA. US-07.
///
/// Estrategia: usa UpdateTicketHandler (prioridad cambiada) para ejercitar
/// GetDueDateFromSlaAsync con startTime = ticket.CreatedAt.
///
/// Schedule de prueba: UTC+0 offset, Lun-Vie (mask=31), 09:00-17:00.
/// Semana de referencia: 2025-01-06 (Lun) a 2025-01-12 (Dom).
/// </summary>
public class SlaBusinessHoursTests
{
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid ActorId  = Guid.NewGuid();

    private static readonly Guid DefaultScheduleId =
        Guid.Parse("00000000-0000-0000-0007-000000000001");

    // ── Factories ────────────────────────────────────────────────────────────

    private static Client MakeClient() =>
        TestEntityFactory.MakeClient(
            id: ClientId, tbnCode: "TBN-TEST",
            fullName: "Cliente Test", phoneMain: "59170000001",
            installationDate: DateTime.UtcNow.AddYears(-1));

    private static AuditService MakeAudit() =>
        new(RepoMock.Empty<AuditLog>().Object, NullLogger<AuditService>.Instance);

    private static Mock<INotifPublisher> MakeNotif()
    {
        var m = new Mock<INotifPublisher>();
        m.Setup(n => n.PublishAsync(
                It.IsAny<NotifType>(), It.IsAny<Guid>(),
                It.IsAny<string?>(), It.IsAny<Dictionary<string, string>>(),
                It.IsAny<Guid?>()))
         .Returns(Task.CompletedTask);
        return m;
    }

    // Horario de prueba: UTC+0, Lun-Vie (mask=31), 09:00-17:00
    private static BusinessSchedule MakeSchedule(IList<ScheduleHoliday>? holidays = null)
    {
        var schedule = new BusinessSchedule
        {
            Id              = DefaultScheduleId,
            Name            = "Horario Prueba",
            TimeZoneName    = "UTC",
            UtcOffsetHours  = 0,
            WorkingDaysMask = 31,   // Lunes=1, Martes=2, Miércoles=4, Jueves=8, Viernes=16
            StartHour       = 9,
            EndHour         = 17,
            Is24x7          = false,
            IsDefault       = true,
            IsActive        = true,
            CreatedAt       = DateTime.UtcNow,
        };
        if (holidays is not null)
            foreach (var h in holidays) schedule.Holidays.Add(h);
        return schedule;
    }

    // Plan SLA 24x7
    private static SlaPlan MakePlan24x7(string priority, int resolutionMinutes) => new()
    {
        Id                   = Guid.NewGuid(),
        Name                 = $"Plan 24x7 {priority}",
        Priority             = priority,
        ResolutionMinutes    = resolutionMinutes,
        FirstResponseMinutes = 30,
        Schedule             = SlaSchedule.Veinticuatro7,
        IsActive             = true,
    };

    // Plan SLA con Schedule = Laboral (sin BusinessScheduleId → usa el default)
    private static SlaPlan MakeLaboralPlan(string priority, int resolutionMinutes) => new()
    {
        Id                   = Guid.NewGuid(),
        Name                 = $"Plan Laboral {priority}",
        Priority             = priority,
        ResolutionMinutes    = resolutionMinutes,
        FirstResponseMinutes = 30,
        Schedule             = SlaSchedule.Laboral,
        IsActive             = true,
    };

    // Ticket con CreatedAt controlado — permite fijar el startTime en GetDueDateFromSlaAsync
    private static SupportTicket MakeTicket(DateTime createdAt,
        TicketPriority priority = TicketPriority.Alta) => new()
    {
        Id          = Guid.NewGuid(),
        ClientId    = ClientId,
        Subject     = "Test SLA",
        Description = "Descripción de prueba",
        Status      = TicketStatus.EnProceso,
        Priority    = priority,
        CreatedAt   = createdAt,
    };

    private static UpdateTicketHandler MakeHandler(
        SupportTicket ticket, SlaPlan plan, BusinessSchedule? schedule = null)
    {
        var ticketRepo   = RepoMock.Of(ticket);
        var commentRepo  = RepoMock.Empty<TicketComment>();
        var workLogRepo  = RepoMock.Empty<TicketWorkLog>();
        var visitRepo    = RepoMock.Empty<TicketVisit>();
        var slaPlanRepo  = RepoMock.Of(plan);
        var notifRepo    = RepoMock.Empty<TicketNotification>();
        var scheduleRepo = schedule is not null
            ? RepoMock.Of(schedule)
            : RepoMock.Empty<BusinessSchedule>();

        var helper = new TicketHelper(
            ticketRepo.Object, commentRepo.Object, workLogRepo.Object,
            visitRepo.Object, slaPlanRepo.Object, notifRepo.Object,
            scheduleRepo.Object, MakeNotif().Object);

        return new UpdateTicketHandler(ticketRepo.Object, commentRepo.Object, helper, MakeAudit());
    }

    // Exercises GetDueDateFromSlaAsync via UpdateTicketHandler (priority change path)
    private static async Task<DateTime?> ComputeDueDate(
        DateTime startUtc, SlaPlan plan, BusinessSchedule schedule)
    {
        var ticket  = MakeTicket(startUtc);
        var handler = MakeHandler(ticket, plan, schedule);

        var result = await handler.Handle(
            new UpdateTicketCommand(
                ticket.Id,
                new UpdateTicketDto { Priority = plan.Priority },
                ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue(because: "UpdateTicketHandler no debe fallar en un ticket válido");
        return result.Value!.DueDate;
    }

    // ── TC-SLA-01: Tiempo dentro del mismo día laboral ───────────────────────

    [Fact]
    public async Task BizMinutes_WithinSameDay_CorrectDueDate()
    {
        // Lunes 10:00 + 120 min → Lunes 12:00
        var start    = new DateTime(2025, 1, 6, 10, 0, 0, DateTimeKind.Utc);
        var plan     = MakeLaboralPlan("Media", 120);
        var schedule = MakeSchedule();

        var dueDate = await ComputeDueDate(start, plan, schedule);

        var expected = new DateTime(2025, 1, 6, 12, 0, 0, DateTimeKind.Utc);
        dueDate!.Value.Should().BeCloseTo(expected, TimeSpan.FromSeconds(5));
    }

    // ── TC-SLA-02: Tiempo que traspasa el fin del día al día siguiente ───────

    [Fact]
    public async Task BizMinutes_SpansToNextDay_CorrectDueDate()
    {
        // Lunes 16:00 + 120 min:
        //   Hoy disponibles: 60 min (16:00→17:00)
        //   Resto: 60 min → Martes 09:00 → Martes 10:00
        var start    = new DateTime(2025, 1, 6, 16, 0, 0, DateTimeKind.Utc);
        var plan     = MakeLaboralPlan("Alta", 120);
        var schedule = MakeSchedule();

        var dueDate = await ComputeDueDate(start, plan, schedule);

        var expected = new DateTime(2025, 1, 7, 10, 0, 0, DateTimeKind.Utc); // Martes 10:00
        dueDate!.Value.Should().BeCloseTo(expected, TimeSpan.FromSeconds(5));
    }

    // ── TC-SLA-03: Empieza en sábado → salta al lunes ─────────────────────

    [Fact]
    public async Task BizMinutes_StartsOnSaturday_SnapsToMonday()
    {
        // Sábado 12:00 → snap a Lunes 09:00 → + 60 min → Lunes 10:00
        var start    = new DateTime(2025, 1, 4, 12, 0, 0, DateTimeKind.Utc); // Sábado
        var plan     = MakeLaboralPlan("Alta", 60);
        var schedule = MakeSchedule();

        var dueDate = await ComputeDueDate(start, plan, schedule);

        var expected = new DateTime(2025, 1, 6, 10, 0, 0, DateTimeKind.Utc); // Lunes 10:00
        dueDate!.Value.Should().BeCloseTo(expected, TimeSpan.FromSeconds(5));
    }

    // ── TC-SLA-04: Empieza en domingo → salta al lunes ───────────────────

    [Fact]
    public async Task BizMinutes_StartsOnSunday_SnapsToMonday()
    {
        var start    = new DateTime(2025, 1, 5, 8, 0, 0, DateTimeKind.Utc); // Domingo
        var plan     = MakeLaboralPlan("Alta", 30);
        var schedule = MakeSchedule();

        var dueDate = await ComputeDueDate(start, plan, schedule);

        var expected = new DateTime(2025, 1, 6, 9, 30, 0, DateTimeKind.Utc);
        dueDate!.Value.Should().BeCloseTo(expected, TimeSpan.FromSeconds(5));
    }

    // ── TC-SLA-05: Empieza antes del horario laboral → snap a inicio ──────

    [Fact]
    public async Task BizMinutes_StartsBeforeBusinessHours_SnapsToStartHour()
    {
        // Lunes 07:00 → snap a 09:00 → + 60 min → Lunes 10:00
        var start    = new DateTime(2025, 1, 6, 7, 0, 0, DateTimeKind.Utc);
        var plan     = MakeLaboralPlan("Alta", 60);
        var schedule = MakeSchedule();

        var dueDate = await ComputeDueDate(start, plan, schedule);

        var expected = new DateTime(2025, 1, 6, 10, 0, 0, DateTimeKind.Utc);
        dueDate!.Value.Should().BeCloseTo(expected, TimeSpan.FromSeconds(5));
    }

    // ── TC-SLA-06: Empieza después del horario laboral → siguiente día ────

    [Fact]
    public async Task BizMinutes_StartsAfterBusinessHours_MovesToNextDayStart()
    {
        // Lunes 18:00 → snap a Martes 09:00 → + 60 min → Martes 10:00
        var start    = new DateTime(2025, 1, 6, 18, 0, 0, DateTimeKind.Utc);
        var plan     = MakeLaboralPlan("Alta", 60);
        var schedule = MakeSchedule();

        var dueDate = await ComputeDueDate(start, plan, schedule);

        var expected = new DateTime(2025, 1, 7, 10, 0, 0, DateTimeKind.Utc);
        dueDate!.Value.Should().BeCloseTo(expected, TimeSpan.FromSeconds(5));
    }

    // ── TC-SLA-07: Viernes al final del día → salta fin de semana completo ─

    [Fact]
    public async Task BizMinutes_FridayLateAfternoon_SkipsWeekendToMonday()
    {
        // Viernes 2025-01-10 16:30 + 120 min:
        //   Disponibles hoy: 30 min (16:30→17:00)
        //   Resto: 90 min → Lunes 2025-01-13 09:00 → Lunes 10:30
        var start    = new DateTime(2025, 1, 10, 16, 30, 0, DateTimeKind.Utc); // Viernes
        var plan     = MakeLaboralPlan("Alta", 120);
        var schedule = MakeSchedule();

        var dueDate = await ComputeDueDate(start, plan, schedule);

        var expected = new DateTime(2025, 1, 13, 10, 30, 0, DateTimeKind.Utc); // Lunes 10:30
        dueDate!.Value.Should().BeCloseTo(expected, TimeSpan.FromSeconds(5));
    }

    // ── TC-SLA-08: Feriado el lunes → salta al martes ─────────────────────

    [Fact]
    public async Task BizMinutes_MondayHoliday_SkipsToTuesday()
    {
        var holidays = new List<ScheduleHoliday>
        {
            new() {
                Id = Guid.NewGuid(),
                BusinessScheduleId = DefaultScheduleId,
                Date = new DateOnly(2025, 1, 6),
                Name = "Feriado Prueba",
            },
        };
        var schedule = MakeSchedule(holidays);
        var start    = new DateTime(2025, 1, 6, 10, 0, 0, DateTimeKind.Utc); // Lunes feriado
        var plan     = MakeLaboralPlan("Alta", 60);

        var dueDate = await ComputeDueDate(start, plan, schedule);

        var expected = new DateTime(2025, 1, 7, 10, 0, 0, DateTimeKind.Utc); // Martes 10:00
        dueDate!.Value.Should().BeCloseTo(expected, TimeSpan.FromSeconds(5));
    }

    // ── TC-SLA-09: Feriados consecutivos Lun-Mar → salta al miércoles ─────

    [Fact]
    public async Task BizMinutes_TwoConsecutiveHolidays_SkipsBoth()
    {
        var holidays = new List<ScheduleHoliday>
        {
            new() { Id = Guid.NewGuid(), BusinessScheduleId = DefaultScheduleId, Date = new DateOnly(2025, 1, 6), Name = "H1" },
            new() { Id = Guid.NewGuid(), BusinessScheduleId = DefaultScheduleId, Date = new DateOnly(2025, 1, 7), Name = "H2" },
        };
        var schedule = MakeSchedule(holidays);
        var start    = new DateTime(2025, 1, 6, 10, 0, 0, DateTimeKind.Utc);
        var plan     = MakeLaboralPlan("Alta", 30);

        var dueDate = await ComputeDueDate(start, plan, schedule);

        var expected = new DateTime(2025, 1, 8, 9, 30, 0, DateTimeKind.Utc); // Miércoles 09:30
        dueDate!.Value.Should().BeCloseTo(expected, TimeSpan.FromSeconds(5));
    }

    // ── TC-SLA-10: Horario 24x7 → tiempo de calendario puro ───────────────

    [Fact]
    public async Task Plan24x7_UsesPureCalendarTime_IgnoresBusinessHours()
    {
        // Sábado mediodía + 240 min = Sábado 16:00 (no salta al lunes)
        var plan   = MakePlan24x7("Media", 240);
        var start  = new DateTime(2025, 1, 4, 12, 0, 0, DateTimeKind.Utc); // Sábado 12:00
        var ticket = MakeTicket(start, TicketPriority.Alta);
        var handler = MakeHandler(ticket, plan, schedule: null); // 24x7 ignora BusinessSchedule

        var result = await handler.Handle(
            new UpdateTicketCommand(
                ticket.Id, new UpdateTicketDto { Priority = "Media" },
                ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var expected = start.AddMinutes(240); // Sábado 16:00 — tiempo de calendario
        result.Value!.DueDate!.Value.Should().BeCloseTo(expected, TimeSpan.FromSeconds(5));
    }

    // ── TC-SLA-11: Sin plan SLA activo → DueDate es null ─────────────────

    [Fact]
    public async Task NoPlan_DueDateIsNull()
    {
        var start  = new DateTime(2025, 1, 6, 10, 0, 0, DateTimeKind.Utc);
        var ticket = MakeTicket(start);

        var ticketRepo   = RepoMock.Of(ticket);
        var commentRepo  = RepoMock.Empty<TicketComment>();
        var workLogRepo  = RepoMock.Empty<TicketWorkLog>();
        var visitRepo    = RepoMock.Empty<TicketVisit>();
        var slaPlanRepo  = RepoMock.Empty<SlaPlan>(); // no hay planes SLA
        var notifRepo    = RepoMock.Empty<TicketNotification>();
        var scheduleRepo = RepoMock.Empty<BusinessSchedule>();

        var helper = new TicketHelper(
            ticketRepo.Object, commentRepo.Object, workLogRepo.Object,
            visitRepo.Object, slaPlanRepo.Object, notifRepo.Object,
            scheduleRepo.Object, MakeNotif().Object);

        var handler = new UpdateTicketHandler(
            ticketRepo.Object, commentRepo.Object, helper, MakeAudit());

        var result = await handler.Handle(
            new UpdateTicketCommand(
                ticket.Id, new UpdateTicketDto { Priority = "Baja" },
                ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.DueDate.Should().BeNull();
    }

    // ── TC-SLA-12: Días laborales personalizados (Lun-Sáb) ────────────────

    [Fact]
    public async Task BizMinutes_CustomMask_SaturdayIsWorkday()
    {
        // Schedule: Lun-Sáb (mask=63). Viernes 16:30 + 120 min.
        // Disponibles: 30 min hoy → Resto 90 min → Sábado 09:00 → Sábado 10:30
        var schedule = MakeSchedule();
        schedule.WorkingDaysMask = 63; // Lun=1, Mar=2, Mié=4, Jue=8, Vie=16, Sáb=32

        var start = new DateTime(2025, 1, 10, 16, 30, 0, DateTimeKind.Utc); // Viernes
        var plan  = MakeLaboralPlan("Alta", 120);

        var dueDate = await ComputeDueDate(start, plan, schedule);

        var expected = new DateTime(2025, 1, 11, 10, 30, 0, DateTimeKind.Utc); // Sábado 10:30
        dueDate!.Value.Should().BeCloseTo(expected, TimeSpan.FromSeconds(5));
    }

    // ── TC-SLA-13: UTC offset Bolivia (-4h) ───────────────────────────────

    [Fact]
    public async Task BizMinutes_BoliviaUtcOffset_ConvertsCorrectly()
    {
        // Schedule: UTC-4 (Bolivia), Lun-Vie, 08:00-18:00
        // UTC 12:00 = Bolivia 08:00 (inicio de jornada)
        // Start UTC 12:00 + 120 min → Bolivia 10:00 = UTC 14:00
        var schedule = MakeSchedule();
        schedule.UtcOffsetHours = -4;
        schedule.StartHour      = 8;
        schedule.EndHour        = 18;

        var start = new DateTime(2025, 1, 6, 12, 0, 0, DateTimeKind.Utc); // Lunes UTC 12:00 = Bolivia 08:00
        var plan  = MakeLaboralPlan("Alta", 120);

        var dueDate = await ComputeDueDate(start, plan, schedule);

        // Bolivia 10:00 = UTC 14:00
        var expected = new DateTime(2025, 1, 6, 14, 0, 0, DateTimeKind.Utc);
        dueDate!.Value.Should().BeCloseTo(expected, TimeSpan.FromSeconds(5));
    }

    // ── TC-SLA-14: Feriado que cae en viernes → salta fin de semana + lunes

    [Fact]
    public async Task BizMinutes_FridayHolidayPlusWeekend_SkipsToTuesday()
    {
        // Viernes 2025-01-10 es feriado → snap a Lunes 09:00 + 60 min → Lunes 10:00
        var holidays = new List<ScheduleHoliday>
        {
            new() {
                Id = Guid.NewGuid(),
                BusinessScheduleId = DefaultScheduleId,
                Date = new DateOnly(2025, 1, 10),
                Name = "Feriado Viernes",
            },
        };
        var schedule = MakeSchedule(holidays);
        var start    = new DateTime(2025, 1, 10, 10, 0, 0, DateTimeKind.Utc); // Viernes feriado
        var plan     = MakeLaboralPlan("Alta", 60);

        var dueDate = await ComputeDueDate(start, plan, schedule);

        var expected = new DateTime(2025, 1, 13, 10, 0, 0, DateTimeKind.Utc); // Lunes 10:00
        dueDate!.Value.Should().BeCloseTo(expected, TimeSpan.FromSeconds(5));
    }

    // ── TC-SLA-15: Plan SLA Laboral sin horario default → fallback calendárico

    [Fact]
    public async Task LaboralPlan_NoDefaultSchedule_FallsBackToCalendarTime()
    {
        // No hay BusinessSchedule en DB (scheduleRepo vacío)
        var plan   = MakeLaboralPlan("Alta", 120);
        var start  = new DateTime(2025, 1, 4, 12, 0, 0, DateTimeKind.Utc); // Sábado
        var ticket = MakeTicket(start);
        var handler = MakeHandler(ticket, plan, schedule: null); // scheduleRepo vacío

        var result = await handler.Handle(
            new UpdateTicketCommand(
                ticket.Id, new UpdateTicketDto { Priority = "Alta" },
                ActorId, "Admin", "127.0.0.1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Sin horario configurado → calendario puro como safety-net
        var expected = start.AddMinutes(120); // Sábado 14:00
        result.Value!.DueDate!.Value.Should().BeCloseTo(expected, TimeSpan.FromSeconds(5));
    }
}
