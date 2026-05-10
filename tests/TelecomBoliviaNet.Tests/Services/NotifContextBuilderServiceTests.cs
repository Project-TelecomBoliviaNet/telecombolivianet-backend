using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using TelecomBoliviaNet.Application.Services.Notifications;
using TelecomBoliviaNet.Domain.Entities.Admin;
using TelecomBoliviaNet.Domain.Entities.Clients;
using TelecomBoliviaNet.Domain.Entities.Plans;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Tests.Helpers;
using Xunit;

namespace TelecomBoliviaNet.Tests.Services;

/// <summary>
/// Tests para NotifContextBuilderService.BuildAsync:
///   - Cliente inexistente → contexto vacío
///   - Cálculo de deuda total (suma saldos pendientes)
///   - Formato de meses_deuda_detalle (vencida vs pendiente)
///   - Facturas de instalación → label "Instalación"
///   - Múltiples facturas → ordenadas por DueDate
///   - QR activo → URL incluida
///   - Sin QR → fallback portal o mensaje de contacto
///   - Nombre → separado en nombre y apellido
/// </summary>
public class NotifContextBuilderServiceTests
{
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid PlanId   = Guid.NewGuid();

    private static Client MakeClient(string fullName = "Carlos Flores")
    {
        var client = TestEntityFactory.MakeClient(
            id:       ClientId,
            fullName: fullName,
            planId:   PlanId);
        client.Zone = "Zona Norte";
        client.Plan = new Plan { Id = PlanId, Name = "Plan Hogar 50MB" };
        return client;
    }

    private static Invoice MakeInvoice(
        int year, int month,
        decimal amount,
        InvoiceStatus status = InvoiceStatus.Pendiente,
        InvoiceType type     = InvoiceType.Mensualidad,
        decimal amountPaid   = 0m)
    {
        var dueDay = DateTime.DaysInMonth(year, month);
        return new Invoice
        {
            Id          = Guid.NewGuid(),
            ClientId    = ClientId,
            Type        = type,
            Status      = status,
            Year        = year,
            Month       = month,
            Amount      = amount,
            AmountPaid  = amountPaid,
            DueDate     = new DateTime(year, month, dueDay),
        };
    }

    private static NotifContextBuilderService MakeService(
        Client?          client   = null,
        Invoice[]?       invoices = null,
        ClientQr?        qr       = null,
        SystemConfig[]?  configs  = null)
    {
        var clientRepo = client is not null
            ? RepoMock.Of(client)
            : RepoMock.Empty<Client>();

        var invoiceRepo = invoices is not null
            ? RepoMock.Of(invoices)
            : RepoMock.Empty<Invoice>();

        var qrRepo = qr is not null
            ? RepoMock.Of(qr)
            : RepoMock.Empty<ClientQr>();

        var sysConfigRepo = configs is not null
            ? RepoMock.Of(configs)
            : RepoMock.Empty<SystemConfig>();

        var configuration = new ConfigurationBuilder().Build();

        return new NotifContextBuilderService(
            clientRepo.Object,
            invoiceRepo.Object,
            qrRepo.Object,
            sysConfigRepo.Object,
            configuration);
    }

    // ── Cliente inexistente ──────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_ClienteInexistente_RetornaContextoVacio()
    {
        var svc = MakeService(client: null);

        var ctx = await svc.BuildAsync(Guid.NewGuid());

        ctx.Should().BeEmpty();
    }

    // ── Deuda total ──────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_ClienteSinFacturas_DeudaEsCero()
    {
        var svc = MakeService(MakeClient(), invoices: []);

        var ctx = await svc.BuildAsync(ClientId);

        ctx["deuda"].Should().Be("0.00");
    }

    [Fact]
    public async Task BuildAsync_UnaFactura_DeudaEsMontoCompleto()
    {
        var svc = MakeService(MakeClient(), invoices:
        [
            MakeInvoice(2026, 1, amount: 150m),
        ]);

        var ctx = await svc.BuildAsync(ClientId);

        ctx["deuda"].Should().Be("150.00");
    }

    [Fact]
    public async Task BuildAsync_DosFacturas_DeudaEsSuma()
    {
        var svc = MakeService(MakeClient(), invoices:
        [
            MakeInvoice(2026, 1, amount: 150m),
            MakeInvoice(2026, 2, amount: 180m),
        ]);

        var ctx = await svc.BuildAsync(ClientId);

        ctx["deuda"].Should().Be("330.00");
    }

    [Fact]
    public async Task BuildAsync_FacturaParcialmentePagada_DescontaLoYaPagado()
    {
        var svc = MakeService(MakeClient(), invoices:
        [
            MakeInvoice(2026, 1, amount: 150m, amountPaid: 50m,
                        status: InvoiceStatus.ParcialmentePagada),
        ]);

        var ctx = await svc.BuildAsync(ClientId);

        ctx["deuda"].Should().Be("100.00");
    }

    // ── meses_deuda_detalle ──────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_FacturaPendiente_MuestraVenceEnDetalle()
    {
        var svc = MakeService(MakeClient(), invoices:
        [
            MakeInvoice(2026, 3, amount: 150m, status: InvoiceStatus.Pendiente),
        ]);

        var ctx = await svc.BuildAsync(ClientId);

        ctx["meses_deuda_detalle"].Should().Contain("Marzo 2026");
        ctx["meses_deuda_detalle"].Should().Contain("vence");
        ctx["meses_deuda_detalle"].Should().Contain("150.00");
    }

    [Fact]
    public async Task BuildAsync_FacturaVencida_MuestraVencidaEnDetalle()
    {
        var svc = MakeService(MakeClient(), invoices:
        [
            MakeInvoice(2026, 1, amount: 150m, status: InvoiceStatus.Vencida),
        ]);

        var ctx = await svc.BuildAsync(ClientId);

        ctx["meses_deuda_detalle"].Should().Contain("Enero 2026");
        ctx["meses_deuda_detalle"].Should().Contain("vencida");
    }

    [Fact]
    public async Task BuildAsync_FacturaInstalacion_MuestraInstalacionEnDetalle()
    {
        var svc = MakeService(MakeClient(), invoices:
        [
            MakeInvoice(2026, 1, amount: 200m, type: InvoiceType.Instalacion),
        ]);

        var ctx = await svc.BuildAsync(ClientId);

        ctx["meses_deuda_detalle"].Should().Contain("Instalación");
    }

    [Fact]
    public async Task BuildAsync_DosFacturas_ListaContieneAmbasLineas()
    {
        var svc = MakeService(MakeClient(), invoices:
        [
            MakeInvoice(2026, 1, amount: 150m, status: InvoiceStatus.Vencida),
            MakeInvoice(2026, 2, amount: 150m, status: InvoiceStatus.Pendiente),
        ]);

        var ctx = await svc.BuildAsync(ClientId);

        var detalle = ctx["meses_deuda_detalle"];
        detalle.Should().Contain("Enero 2026");
        detalle.Should().Contain("Febrero 2026");
        // Dos líneas separadas por salto de línea
        detalle.Split('\n').Should().HaveCount(2);
    }

    [Fact]
    public async Task BuildAsync_SinFacturas_DetalleEsMensajeSinPendientes()
    {
        var svc = MakeService(MakeClient(), invoices: []);

        var ctx = await svc.BuildAsync(ClientId);

        ctx["meses_deuda_detalle"].Should().Contain("Sin facturas pendientes");
    }

    // ── Cantidad de meses pendientes ─────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_TresFacturas_MesesPendientesEsTres()
    {
        var svc = MakeService(MakeClient(), invoices:
        [
            MakeInvoice(2025, 11, amount: 150m),
            MakeInvoice(2025, 12, amount: 150m),
            MakeInvoice(2026, 1,  amount: 150m),
        ]);

        var ctx = await svc.BuildAsync(ClientId);

        ctx["meses_pendientes"].Should().Be("3");
    }

    // ── Separación nombre / apellido ─────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_NombreCompleto_SeparaNombreYApellido()
    {
        var svc = MakeService(MakeClient("Carlos Flores"));

        var ctx = await svc.BuildAsync(ClientId);

        ctx["nombre"].Should().Be("Carlos");
        ctx["apellido"].Should().Be("Flores");
        ctx["nombre_completo"].Should().Be("Carlos Flores");
    }

    [Fact]
    public async Task BuildAsync_SoloUnNombre_ApellidoEsVacio()
    {
        var svc = MakeService(MakeClient("Carlos"));

        var ctx = await svc.BuildAsync(ClientId);

        ctx["nombre"].Should().Be("Carlos");
        ctx["apellido"].Should().Be(string.Empty);
    }

    // ── Datos del plan y zona ────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_DevuelvePlanYZonaDelCliente()
    {
        var svc = MakeService(MakeClient());

        var ctx = await svc.BuildAsync(ClientId);

        ctx["plan"].Should().Be("Plan Hogar 50MB");
        ctx["zona"].Should().Be("Zona Norte");
    }

    // ── QR de pago ───────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_QrActivoConUrlAbsoluta_IncluirUrlEnContexto()
    {
        var qr = new ClientQr
        {
            Id       = Guid.NewGuid(),
            ClientId = ClientId,
            ImageUrl = "https://cdn.telecom.bo/qr/carlos.png",
            IsActive = true,
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            UploadedAt = DateTime.UtcNow,
        };

        var svc = MakeService(MakeClient(), qr: qr);

        var ctx = await svc.BuildAsync(ClientId);

        ctx["qr_enlace"].Should().Be("https://cdn.telecom.bo/qr/carlos.png");
    }

    [Fact]
    public async Task BuildAsync_SinQrNiPortal_MensajeFallback()
    {
        var svc = MakeService(MakeClient());  // sin QR ni SystemConfig

        var ctx = await svc.BuildAsync(ClientId);

        ctx["qr_enlace"].Should().Contain("Contáctenos");
    }

    [Fact]
    public async Task BuildAsync_QrExpirado_UsaFallback()
    {
        var qrExpirado = new ClientQr
        {
            Id        = Guid.NewGuid(),
            ClientId  = ClientId,
            ImageUrl  = "https://cdn.telecom.bo/qr/viejo.png",
            IsActive  = true,
            ExpiresAt = DateTime.UtcNow.AddDays(-1),  // ya expiró
            UploadedAt = DateTime.UtcNow.AddDays(-31),
        };

        var svc = MakeService(MakeClient(), qr: qrExpirado);

        var ctx = await svc.BuildAsync(ClientId);

        ctx["qr_enlace"].Should().Contain("Contáctenos");
    }

    // ── Empresa desde SystemConfig ───────────────────────────────────────────

    [Fact]
    public async Task BuildAsync_EmpresaDesdeSysConfig_RetornaValorConfig()
    {
        var configs = new[]
        {
            new SystemConfig { Id = Guid.NewGuid(), Key = "ISP:NombreEmpresa", Value = "TelecomBoliviaNet" },
        };

        var svc = MakeService(MakeClient(), configs: configs);

        var ctx = await svc.BuildAsync(ClientId);

        ctx["empresa"].Should().Be("TelecomBoliviaNet");
    }

    [Fact]
    public async Task BuildAsync_SinConfigEmpresa_UsaDefault()
    {
        var svc = MakeService(MakeClient());  // sin SystemConfig

        var ctx = await svc.BuildAsync(ClientId);

        ctx["empresa"].Should().Be("TelecomBoliviaNet");
    }
}
