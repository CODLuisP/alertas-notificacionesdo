using GeocercaNotificationsService.Models;

namespace GeocercaNotificationsService.Services;

/// <summary>
/// Servicio que orquesta la lógica de negocio para el envío de notificaciones por WhatsApp:
/// verificación de geocercas, filtros de vehículos y tipos de evento, despacho a destinatarios y control de reintentos.
/// </summary>
public interface IWhatsAppNotificationService
{
    /// <summary>
    /// Procesa un evento individual de deviceevent para el canal de WhatsApp.
    /// </summary>
    /// <param name="evt">Evento de entrada o salida de geocerca.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>True si el evento fue procesado con éxito (enviado o descartado según reglas).</returns>
    Task<bool> ProcessEventAsync(DeviceEvent evt, CancellationToken ct);

    /// <summary>
    /// Construye el texto del mensaje formateado para WhatsApp según el tipo de evento (entrada o salida).
    /// </summary>
    Task<string> BuildMessageTextAsync(DeviceEvent evt, string geofenceName, CancellationToken ct);
}
