namespace GeocercaNotificationsService.Models;

/// <summary>
/// Representa una fila de la tabla gts.deviceevent.
/// </summary>
public sealed class DeviceEvent
{
    public int Id { get; set; }
    public string AccountID { get; set; } = string.Empty;
    public string DeviceID { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Unix timestamp (segundos desde epoch). La columna en MySQL es INT.
    /// </summary>
    public long ServerTime { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Speed { get; set; }
    public bool Ignition { get; set; }
    public string? AlarmType { get; set; }
    public int? GeofenceID { get; set; }
    public string? GeofenceName { get; set; }
    public bool IsEnviado { get; set; }

    /// <summary>
    /// 0 = Pendiente de envío por WhatsApp, 1 = Enviado / Descartado, 2 = Falló tras máximos reintentos.
    /// </summary>
    public int IsEnviadoWhatsapp { get; set; }

    /// <summary>
    /// Fecha y hora en que se completó el envío por WhatsApp.
    /// </summary>
    public DateTime? FechaEnvioWhatsapp { get; set; }

    /// <summary>
    /// Contador de intentos fallidos de envío por WhatsApp (máx. 5).
    /// </summary>
    public int IntentosWhatsapp { get; set; }

    /// <summary>
    /// Convierte el Unix timestamp a DateTime UTC.
    /// </summary>
    public DateTime ServerTimeUtc =>
        DateTimeOffset.FromUnixTimeSeconds(ServerTime).UtcDateTime;
}
