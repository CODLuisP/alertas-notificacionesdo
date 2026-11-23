namespace GeocercaNotificationsService.Models;

/// <summary>
/// Payload que se envía al frontend vía SignalR bajo el evento "DeviceAlert"
/// para alertas de dispositivo (alarm, deviceOverspeed, ignitionOn/Off, deviceOnline/Offline/Unknown).
/// </summary>
public sealed class DeviceAlertPayload
{
    public int Id { get; set; }
    public string AccountID { get; set; } = string.Empty;
    public string DeviceID { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? AlarmType { get; set; }

    /// <summary>
    /// Clave normalizada del catálogo (ej: "alarm:sos", "deviceOverspeed", "ignitionOn").
    /// </summary>
    public string EventKey { get; set; } = string.Empty;

    /// <summary>
    /// Nivel de severidad: "critical", "warning" o "info".
    /// </summary>
    public string Severity { get; set; } = "info";

    /// <summary>
    /// Unix timestamp original (segundos desde epoch).
    /// </summary>
    public long ServerTime { get; set; }

    /// <summary>
    /// Fecha/hora legible en formato ISO 8601 UTC.
    /// </summary>
    public string ServerTimeUtc { get; set; } = string.Empty;

    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Speed { get; set; }
    public bool Ignition { get; set; }

    /// <summary>
    /// Configuración de sonido elegida por la cuenta para esta alerta.
    /// </summary>
    public string? Sonido { get; set; }
    public int? Volumen { get; set; }
    public bool? RepetirHastaOk { get; set; }
}
