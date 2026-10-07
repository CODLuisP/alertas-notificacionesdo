namespace GeocercaNotificationsService.Models;

/// <summary>
/// Payload que se envía al frontend vía SignalR cuando ocurre un evento de geocerca.
/// </summary>
public sealed class GeofenceAlertPayload
{
    public string AccountID { get; set; } = string.Empty;
    public string DeviceID { get; set; } = string.Empty;
    public int? GeofenceID { get; set; }
    public string? GeofenceName { get; set; }
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Unix timestamp original (segundos desde epoch).
    /// </summary>
    public long ServerTime { get; set; }

    /// <summary>
    /// Fecha/hora legible en formato ISO 8601 UTC, para facilidad del frontend.
    /// </summary>
    public string ServerTimeUtc { get; set; } = string.Empty;

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    /// <summary>
    /// Duración en minutos dentro de la geocerca (solo para geofenceExit).
    /// Null si es geofenceEnter o si no se encontró el enter previo.
    /// </summary>
    public int? DurationMinutes { get; set; }
}
