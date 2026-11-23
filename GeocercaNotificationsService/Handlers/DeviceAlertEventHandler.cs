using GeocercaNotificationsService.Hubs;
using GeocercaNotificationsService.Models;
using GeocercaNotificationsService.Repositories;
using Microsoft.AspNetCore.SignalR;

namespace GeocercaNotificationsService.Handlers;

/// <summary>
/// Handler para alertas de dispositivo provenientes de Traccar (alarm, deviceOverspeed,
/// ignitionOn/Off, deviceOnline/Offline/Unknown).
/// Solo emite por SignalR ("DeviceAlert") si la cuenta tiene activada esa alerta
/// en su configuración SQLite (cuenta_alertas_config).
/// </summary>
public sealed class DeviceAlertEventHandler : IEventHandler
{
    private readonly IHubContext<NotificationsHub> _hubContext;
    private readonly IConfiguracionRepository _configRepo;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DeviceAlertEventHandler> _logger;

    public DeviceAlertEventHandler(
        IHubContext<NotificationsHub> hubContext,
        IConfiguracionRepository configRepo,
        IConfiguration configuration,
        ILogger<DeviceAlertEventHandler> logger)
    {
        _hubContext = hubContext;
        _configRepo = configRepo;
        _configuration = configuration;
        _logger = logger;
    }

    public IReadOnlyList<string> SupportedEventTypes { get; } = new[]
    {
        "alarm",
        "deviceOverspeed",
        "ignitionOn",
        "ignitionOff",
        "deviceOnline",
        "deviceOffline",
        "deviceUnknown"
    };

    public async Task<bool> HandleAsync(DeviceEvent evt, CancellationToken ct)
    {
        // Seguro adicional: no emitir alertas antiguas por SignalR
        var maxAgeMinutes = _configuration.GetValue("AlertMaxAgeMinutes", 5);
        if (maxAgeMinutes > 0)
        {
            var ageSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - evt.ServerTime;
            if (ageSeconds > maxAgeMinutes * 60L)
            {
                _logger.LogDebug(
                    "Evento antiguo descartado para SignalR: id={EventId}, eventType={EventType}, age={Age}s",
                    evt.Id, evt.EventType, ageSeconds);
                return true;
            }
        }

        var eventKey = AlertEventCatalog.ResolveEventKey(evt.EventType, evt.AlarmType);

        // Verificar si la cuenta tiene esta alerta agregada/activa en "Mis notificaciones"
        var alertConfig = await _configRepo.GetActiveAlertConfigAsync(evt.AccountID, eventKey, ct);
        if (alertConfig is null)
        {
            _logger.LogDebug(
                "Alerta '{EventKey}' desactivada para accountID='{AccountID}', evento id={EventId} omitido.",
                eventKey, evt.AccountID, evt.Id);
            return true;
        }

        var payload = new DeviceAlertPayload
        {
            Id = evt.Id,
            AccountID = evt.AccountID,
            DeviceID = evt.DeviceID,
            EventType = evt.EventType,
            AlarmType = evt.AlarmType,
            EventKey = eventKey,
            Severity = AlertEventCatalog.GetSeverity(eventKey),
            ServerTime = evt.ServerTime,
            ServerTimeUtc = evt.ServerTimeUtc.ToString("o"),
            Latitude = evt.Latitude,
            Longitude = evt.Longitude,
            Speed = evt.Speed ?? 0,
            Ignition = evt.Ignition ?? false,
            Sonido = alertConfig.Sonido,
            Volumen = alertConfig.Volumen,
            RepetirHastaOk = alertConfig.RepetirHastaOk
        };

        await _hubContext.Clients
            .Group(evt.AccountID)
            .SendAsync("DeviceAlert", payload, ct);

        _logger.LogDebug(
            "DeviceAlert enviada por SignalR: accountID={AccountID}, deviceID={DeviceID}, eventKey={EventKey}, id={EventId}",
            evt.AccountID, evt.DeviceID, eventKey, evt.Id);

        return true;
    }
}
