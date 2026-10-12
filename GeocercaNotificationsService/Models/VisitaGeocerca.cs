namespace GeocercaNotificationsService.Models;

/// <summary>
/// Representa una visita completa a una geocerca (entrada + salida ya resuelta con duración calculada).
/// </summary>
public sealed class VisitaGeocerca
{
    /// <summary>
    /// Identificador único del registro de visita en visitas_geocerca.
    /// </summary>
    /// <example>42</example>
    public int Id { get; set; }

    /// <summary>
    /// Identificador de la cuenta propietaria (ej: 'movilbus').
    /// </summary>
    /// <example>movilbus</example>
    public string AccountID { get; set; } = string.Empty;

    /// <summary>
    /// Placa o identificador del vehículo.
    /// </summary>
    /// <example>ABC-123</example>
    public string DeviceID { get; set; } = string.Empty;

    /// <summary>
    /// ID de la geocerca visitada.
    /// </summary>
    /// <example>15</example>
    public int GeofenceID { get; set; }

    /// <summary>
    /// Nombre descriptivo de la geocerca visitada.
    /// </summary>
    /// <example>Planta Central Lima</example>
    public string? GeofenceName { get; set; }

    /// <summary>
    /// Fecha y hora UTC en que el vehículo ingresó a la geocerca. Null si fue una entrada incompleta.
    /// </summary>
    /// <example>2026-09-22T14:30:00Z</example>
    public DateTime? FechaEntrada { get; set; }

    /// <summary>
    /// Fecha y hora UTC en que el vehículo salió de la geocerca.
    /// </summary>
    /// <example>2026-09-22T15:15:00Z</example>
    public DateTime FechaSalida { get; set; }

    /// <summary>
    /// Duración total de la permanencia dentro de la geocerca en minutos. Null si no hubo geofenceEnter previo.
    /// </summary>
    /// <example>45</example>
    public int? DuracionMinutos { get; set; }

    /// <summary>
    /// Latitud GPS registrada al momento de la entrada.
    /// </summary>
    /// <example>-12.046374</example>
    public decimal? LatitudEntrada { get; set; }

    /// <summary>
    /// Longitud GPS registrada al momento de la entrada.
    /// </summary>
    /// <example>-77.042793</example>
    public decimal? LongitudEntrada { get; set; }

    /// <summary>
    /// Latitud GPS registrada al momento de la salida.
    /// </summary>
    /// <example>-12.046390</example>
    public decimal? LatitudSalida { get; set; }

    /// <summary>
    /// Longitud GPS registrada al momento de la salida.
    /// </summary>
    /// <example>-77.042810</example>
    public decimal? LongitudSalida { get; set; }

    /// <summary>
    /// ID del evento geofenceEnter original en la tabla deviceevent. Null si entrada_incompleta es 1.
    /// </summary>
    /// <example>10025</example>
    public int? EventoEntradaId { get; set; }

    /// <summary>
    /// ID del evento geofenceExit original en la tabla deviceevent.
    /// </summary>
    /// <example>10048</example>
    public int EventoSalidaId { get; set; }

    /// <summary>
    /// Indica si la visita no tuvo un geofenceEnter previo registrado (false = Completa, true = Entrada incompleta).
    /// </summary>
    /// <example>false</example>
    public bool EntradaIncompleta { get; set; }

    /// <summary>
    /// true si la visita terminó porque se desvinculó el vehículo de la geocerca estando adentro
    /// (el vehículo no salió; la "salida" es el momento de la desvinculación).
    /// </summary>
    /// <example>false</example>
    public bool SalidaPorDesvinculacion { get; set; }

    /// <summary>
    /// Fecha y hora en que se registró la fila en la tabla visitas_geocerca.
    /// </summary>
    /// <example>2026-09-22T15:15:02Z</example>
    public DateTime FechaRegistro { get; set; }
}

/// <summary>
/// Resultado consolidado del reporte de visitas agrupado por vehículo (deviceID) y geocerca (geofenceID).
/// </summary>
public sealed class ResumenVisitaGeocerca
{
    /// <summary>
    /// Placa o identificador del vehículo.
    /// </summary>
    /// <example>ABC-123</example>
    public string DeviceID { get; set; } = string.Empty;

    /// <summary>
    /// ID de la geocerca.
    /// </summary>
    /// <example>15</example>
    public int GeofenceID { get; set; }

    /// <summary>
    /// Nombre descriptivo de la geocerca.
    /// </summary>
    /// <example>Planta Central Lima</example>
    public string? GeofenceName { get; set; }

    /// <summary>
    /// Número total de visitas realizadas por este vehículo a esta geocerca en el rango consultado.
    /// </summary>
    /// <example>8</example>
    public int TotalVisitas { get; set; }

    /// <summary>
    /// Suma acumulada de minutos de permanencia de todas las visitas en el rango consultado.
    /// </summary>
    /// <example>360</example>
    public long MinutosTotales { get; set; }

    /// <summary>
    /// Promedio de permanencia en minutos por cada visita.
    /// </summary>
    /// <example>45</example>
    public int MinutosPromedioPorVisita { get; set; }
}

/// <summary>
/// Respuesta paginada para el listado de visitas de geocerca.
/// </summary>
public sealed class VisitasPaginadas
{
    /// <summary>
    /// Colección de visitas correspondientes a la página solicitada.
    /// </summary>
    public IEnumerable<VisitaGeocerca> Visitas { get; set; } = Enumerable.Empty<VisitaGeocerca>();

    /// <summary>
    /// Cantidad total de registros que coinciden con los filtros aplicados.
    /// </summary>
    /// <example>142</example>
    public int TotalRegistros { get; set; }

    /// <summary>
    /// Número de la página actual devuelta (inicia en 1).
    /// </summary>
    /// <example>1</example>
    public int PaginaActual { get; set; }

    /// <summary>
    /// Cantidad total de páginas disponibles según el tamaño de página configurado.
    /// </summary>
    /// <example>8</example>
    public int TotalPaginas { get; set; }

    /// <summary>
    /// Tamaño de página utilizado para la consulta (máximo 100).
    /// </summary>
    /// <example>20</example>
    public int PageSize { get; set; }
}

/// <summary>
/// Modelo estándar para mensajes de error de la API.
/// </summary>
public sealed class ErrorResponse
{
    /// <summary>
    /// Mensaje descriptivo de la causa del error.
    /// </summary>
    /// <example>fechaDesde no puede ser posterior a fechaHasta</example>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Detalle de excepción técnica si ocurrió un error interno (500).
    /// </summary>
    /// <example>null</example>
    public string? Error { get; set; }
}

/// <summary>
/// Representa una alerta individual de geocerca registrada en la tabla deviceevent.
/// </summary>
public sealed class AlertaGeocerca
{
    /// <summary>
    /// ID único del evento en deviceevent.
    /// </summary>
    /// <example>28903</example>
    public int Id { get; set; }

    /// <summary>
    /// Cuenta a la que pertenece el vehículo.
    /// </summary>
    /// <example>movilbus</example>
    public string AccountID { get; set; } = string.Empty;

    /// <summary>
    /// Placa del vehículo.
    /// </summary>
    /// <example>s110-cml340</example>
    public string DeviceID { get; set; } = string.Empty;

    /// <summary>
    /// Tipo de evento ('geofenceEnter' o 'geofenceExit').
    /// </summary>
    /// <example>geofenceEnter</example>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp Unix en segundos.
    /// </summary>
    /// <example>1790196409</example>
    public long ServerTime { get; set; }

    /// <summary>
    /// Fecha y hora UTC calculada a partir de ServerTime.
    /// </summary>
    public DateTime ServerTimeUtc => DateTimeOffset.FromUnixTimeSeconds(ServerTime).UtcDateTime;

    /// <summary>
    /// Latitud registrada.
    /// </summary>
    /// <example>-12.046374</example>
    public double Latitude { get; set; }

    /// <summary>
    /// Longitud registrada.
    /// </summary>
    /// <example>-77.042793</example>
    public double Longitude { get; set; }

    /// <summary>
    /// Velocidad registrada.
    /// </summary>
    public double Speed { get; set; }

    /// <summary>
    /// ID de la geocerca.
    /// </summary>
    /// <example>5</example>
    public int? GeofenceID { get; set; }

    /// <summary>
    /// Nombre de la geocerca.
    /// </summary>
    /// <example>Prueba</example>
    public string? GeofenceName { get; set; }

    /// <summary>
    /// 1 si ya fue emitido por SignalR, 0 si pendiente.
    /// </summary>
    public bool IsEnviado { get; set; }
}
