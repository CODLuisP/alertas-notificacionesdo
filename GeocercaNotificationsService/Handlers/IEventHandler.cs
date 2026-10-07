using GeocercaNotificationsService.Models;

namespace GeocercaNotificationsService.Handlers;

/// <summary>
/// Interfaz para los handlers de eventos de dispositivo.
/// Implementa esta interfaz para agregar soporte a un nuevo tipo de evento
/// (ej: alarm, ignitionOn, ignitionOff, speedAlarm, etc.)
/// </summary>
public interface IEventHandler
{
    /// <summary>
    /// Los tipos de evento que este handler puede procesar.
    /// Ejemplo: ["geofenceEnter", "geofenceExit"]
    /// </summary>
    IReadOnlyList<string> SupportedEventTypes { get; }

    /// <summary>
    /// Procesa un evento individual: construye el payload, lo envía por SignalR
    /// al grupo correspondiente y retorna true si el envío fue exitoso.
    /// </summary>
    Task<bool> HandleAsync(DeviceEvent evt, CancellationToken ct);
}
