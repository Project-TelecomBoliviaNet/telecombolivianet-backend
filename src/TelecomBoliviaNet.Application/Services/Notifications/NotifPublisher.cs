using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelecomBoliviaNet.Application.Interfaces;
using TelecomBoliviaNet.Domain.Entities.Notifications;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Services.Notifications;

/// <summary>
/// Implementación de INotifPublisher.
/// Inserta en notif_outbox respetando la configuración activa.
/// Se usa dentro de transacciones de negocio (SuspendAsync, ReactivateAsync, etc.).
///
/// CORRECCIONES:
///   - OutboxId = null en logs OMITIDO (no Guid.Empty — FK inválido)
///   - Si contexto llega vacío, lo construye automáticamente vía NotifContextBuilderService
/// </summary>
public class NotifPublisher : INotifPublisher
{
    private readonly IGenericRepository<NotifConfig>     _configRepo;
    private readonly IGenericRepository<NotifOutbox>     _outboxRepo;
    private readonly IGenericRepository<NotifLog>        _logRepo;
    private readonly NotifContextBuilderService          _contextBuilder;
    private readonly ILogger<NotifPublisher>             _logger;

    public NotifPublisher(
        IGenericRepository<NotifConfig>  configRepo,
        IGenericRepository<NotifOutbox>  outboxRepo,
        IGenericRepository<NotifLog>     logRepo,
        NotifContextBuilderService       contextBuilder,
        ILogger<NotifPublisher>          logger)
    {
        _configRepo     = configRepo;
        _outboxRepo     = outboxRepo;
        _logRepo        = logRepo;
        _contextBuilder = contextBuilder;
        _logger         = logger;
    }

    public async Task PublishAsync(
        NotifType                  tipo,
        Guid                       clienteId,
        string?                    phoneNumber,
        Dictionary<string, string> contexto,
        Guid?                      referenciaId = null)
    {
        // 1. Verificar config activa
        var config = await _configRepo.GetAll()
            .FirstOrDefaultAsync(c => c.Tipo == tipo);

        if (config is null || !config.Activo)
        {
            _logger.LogDebug("NotifPublisher: tipo {Tipo} desactivado o sin config. Omitido.", tipo);
            return;
        }

        // 2. Si el cliente no tiene teléfono → log OMITIDO con OutboxId = null
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            _logger.LogWarning("NotifPublisher: cliente {Id} sin teléfono para tipo {Tipo}. OMITIDO.", clienteId, tipo);
            await _logRepo.AddAsync(new NotifLog
            {
                OutboxId     = null,   // sin outbox asociado
                ClienteId    = clienteId,
                Tipo         = tipo,
                PhoneNumber  = string.Empty,
                Mensaje      = string.Empty,
                Estado       = NotifLogEstado.OMITIDO,
                IntentoNum   = 0,
                ErrorDetalle = "Cliente sin PhoneMain registrado",
                RegistradoAt = DateTime.UtcNow,
            });
            return;
        }

        // 3. Si el contexto llega vacío, construirlo automáticamente
        if (contexto.Count == 0)
        {
            _logger.LogDebug("NotifPublisher: contexto vacío para {Tipo} cliente={Id}. Auto-construyendo.", tipo, clienteId);
            contexto = await _contextBuilder.BuildAsync(clienteId);
        }

        // 4. Calcular ventana de envío
        var enviarDesde = DateTime.UtcNow.AddSeconds(config.DelaySegundos);

        var outbox = new NotifOutbox
        {
            Tipo         = tipo,
            ClienteId    = clienteId,
            PhoneNumber  = phoneNumber,
            Publicado    = false,
            Intentos     = 0,
            EnviarDesde  = enviarDesde,
            EstadoFinal  = null,
            CreadoAt     = DateTime.UtcNow,
            ContextoJson = JsonSerializer.Serialize(contexto),
            ReferenciaId = referenciaId,
        };

        await _outboxRepo.AddAsync(outbox);
        _logger.LogInformation(
            "NotifPublisher: outbox insertado tipo={Tipo} cliente={Id} enviarDesde={Desde}",
            tipo, clienteId, enviarDesde);
    }
}
