namespace GeocercaNotificationsService.Models;

/// <summary>
/// Elemento del catálogo oficial de alertas soportadas por el sistema.
/// </summary>
public sealed class CatalogoAlertaItem
{
    public string EventKey { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string? AlarmType { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Descripcion { get; set; } = string.Empty;
    public string Severidad { get; set; } = "info";
    public string SonidoDefault { get; set; } = "clasico";
}

/// <summary>
/// Preferencias generales de visualización del mapa (pestaña "Preferencias").
/// </summary>
public sealed class PreferenciasVisualizacionDto
{
    /// <summary>
    /// Tipo de mapa por defecto: "mapa", "satelite", "hibrido" o "relieve".
    /// </summary>
    public string TipoMapa { get; set; } = "mapa";
    public bool MostrarPlacas { get; set; } = true;
    public bool AgruparUnidades { get; set; } = false;
    public bool SeguirUnidad { get; set; } = true;
}

/// <summary>
/// Configuración global de sonido y horario "No molestar" (pestaña "Alertas y sonidos").
/// </summary>
public sealed class SonidoGlobalDto
{
    public bool SilenciarTodo { get; set; } = false;
    public int VolumenGeneral { get; set; } = 100;
    public bool NoMolestarActivo { get; set; } = false;
    public string NoMolestarInicio { get; set; } = "22:00";
    public string NoMolestarFin { get; set; } = "07:00";
}

/// <summary>
/// Configuración individual de una notificación activa en "Mis notificaciones".
/// </summary>
public sealed class AlertaNotificacionConfigDto
{
    /// <summary>
    /// Clave del evento (ej: "geofenceEnter", "geofenceExit", "deviceOverspeed",
    /// "alarm:sos", "alarm:powercut", "alarm:overspeed", "ignitionOn", "ignitionOff",
    /// "deviceOnline", "deviceOffline", "deviceUnknown").
    /// </summary>
    public string EventKey { get; set; } = string.Empty;
    public string Sonido { get; set; } = "clasico";
    public int Volumen { get; set; } = 80;
    public bool RepetirHastaOk { get; set; } = false;
}

/// <summary>
/// Request para guardar la pestaña "Alertas y sonidos".
/// </summary>
public sealed class GuardarAlertasConfigRequest
{
    public SonidoGlobalDto SonidoGlobal { get; set; } = new();
    public List<AlertaNotificacionConfigDto> Notificaciones { get; set; } = new();
}

/// <summary>
/// Configuración de límites de velocidad (pestaña "Límites de velocidad").
/// </summary>
public class LimitesVelocidadDto
{
    /// <summary>
    /// Modo de aplicación: "flota" o "vehiculo".
    /// </summary>
    public string Modo { get; set; } = "flota";
    public int LimiteFlotaKmh { get; set; } = 90;

    /// <summary>
    /// Límites específicos por placa/deviceID (ej: { "306-DAL462": 80 }).
    /// </summary>
    public Dictionary<string, int> PorVehiculo { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Respuesta del PUT /api/configuracion/limites-velocidad incluyendo el resumen de sincronización con Traccar.
/// </summary>
public sealed class GuardarLimitesVelocidadResponse : LimitesVelocidadDto
{
    public TraccarSpeedLimitSyncResult? TraccarSync { get; set; }
}

/// <summary>
/// Request para asignar íconos a una o varias unidades (pestaña "Íconos").
/// </summary>
public sealed class GuardarIconosRequest
{
    /// <summary>
    /// Mapa de deviceID -> tipo de ícono (ej: "auto", "cisterna", "van").
    /// Enviar "auto" o vacío elimina el override para esa unidad.
    /// </summary>
    public Dictionary<string, string> Iconos { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Si es true, reemplaza toda la lista de íconos de la cuenta; si es false, hace merge solo con las placas enviadas.
    /// </summary>
    public bool ReemplazarTodo { get; set; } = false;
}

/// <summary>
/// Respuesta completa con toda la configuración de una cuenta.
/// </summary>
public sealed class ConfiguracionCuentaResponse
{
    public string AccountID { get; set; } = string.Empty;
    public PreferenciasVisualizacionDto Preferencias { get; set; } = new();
    public SonidoGlobalDto SonidoGlobal { get; set; } = new();
    public List<AlertaNotificacionConfigDto> Notificaciones { get; set; } = new();
    public LimitesVelocidadDto LimitesVelocidad { get; set; } = new();
    public Dictionary<string, string> Iconos { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? UpdatedAt { get; set; }
}

/// <summary>
/// Catálogo centralizado de alertas soportadas y resolución de claves de evento.
/// </summary>
public static class AlertEventCatalog
{
    public static IReadOnlyList<CatalogoAlertaItem> Items { get; } = new List<CatalogoAlertaItem>
    {
        new()
        {
            EventKey = "geofenceEnter",
            EventType = "geofenceEnter",
            Nombre = "Entrada a geocerca",
            Descripcion = "Cuando una unidad ingresa a una geocerca",
            Severidad = "info",
            SonidoDefault = "ding"
        },
        new()
        {
            EventKey = "geofenceExit",
            EventType = "geofenceExit",
            Nombre = "Salida de geocerca",
            Descripcion = "Cuando una unidad sale de una geocerca",
            Severidad = "info",
            SonidoDefault = "campana"
        },
        new()
        {
            EventKey = "deviceOverspeed",
            EventType = "deviceOverspeed",
            Nombre = "Exceso de velocidad",
            Descripcion = "Cuando una unidad supera el límite de velocidad",
            Severidad = "warning",
            SonidoDefault = "clasico"
        },
        new()
        {
            EventKey = "alarm:sos",
            EventType = "alarm",
            AlarmType = "sos",
            Nombre = "Botón de pánico (SOS)",
            Descripcion = "Alarma de emergencia activada desde la unidad",
            Severidad = "critical",
            SonidoDefault = "sirena"
        },
        new()
        {
            EventKey = "alarm:powercut",
            EventType = "alarm",
            AlarmType = "powercut",
            Nombre = "Corte de energía",
            Descripcion = "Desconexión de batería o alimentación del GPS",
            Severidad = "critical",
            SonidoDefault = "sirena"
        },
        new()
        {
            EventKey = "alarm:overspeed",
            EventType = "alarm",
            AlarmType = "overspeed",
            Nombre = "Alarma de exceso de velocidad (GPS)",
            Descripcion = "Alarma interna de exceso de velocidad reportada por el equipo GPS",
            Severidad = "warning",
            SonidoDefault = "clasico"
        },
        new()
        {
            EventKey = "ignitionOn",
            EventType = "ignitionOn",
            Nombre = "Encendido de motor",
            Descripcion = "Cuando se enciende el motor de una unidad",
            Severidad = "info",
            SonidoDefault = "ding"
        },
        new()
        {
            EventKey = "ignitionOff",
            EventType = "ignitionOff",
            Nombre = "Apagado de motor",
            Descripcion = "Cuando se apaga el motor de una unidad",
            Severidad = "info",
            SonidoDefault = "ding"
        },
        new()
        {
            EventKey = "deviceOnline",
            EventType = "deviceOnline",
            Nombre = "Unidad en línea",
            Descripcion = "Cuando una unidad vuelve a conectarse",
            Severidad = "info",
            SonidoDefault = "ding"
        },
        new()
        {
            EventKey = "deviceOffline",
            EventType = "deviceOffline",
            Nombre = "Unidad desconectada",
            Descripcion = "Cuando una unidad pierde conexión con el servidor",
            Severidad = "warning",
            SonidoDefault = "campana"
        },
        new()
        {
            EventKey = "deviceUnknown",
            EventType = "deviceUnknown",
            Nombre = "Estado desconocido",
            Descripcion = "Cuando no se puede determinar el estado de conexión de una unidad",
            Severidad = "warning",
            SonidoDefault = "campana"
        }
    };

    private static readonly Dictionary<string, CatalogoAlertaItem> ByKey =
        Items.ToDictionary(x => x.EventKey, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Convierte un eventType + alarmType de Traccar a su eventKey normalizado.
    /// Ejemplo: ("alarm", "sos") -> "alarm:sos", ("deviceOverspeed", null) -> "deviceOverspeed".
    /// </summary>
    public static string ResolveEventKey(string eventType, string? alarmType)
    {
        if (string.Equals(eventType, "alarm", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(alarmType))
        {
            return $"alarm:{alarmType.Trim().ToLowerInvariant()}";
        }

        return eventType.Trim();
    }

    public static bool IsValidEventKey(string? eventKey)
    {
        if (string.IsNullOrWhiteSpace(eventKey)) return false;
        return ByKey.ContainsKey(eventKey.Trim());
    }

    public static string GetCanonicalEventKey(string eventKey)
    {
        return ByKey.TryGetValue(eventKey.Trim(), out var item) ? item.EventKey : eventKey.Trim();
    }

    public static string GetSeverity(string eventKey)
    {
        return ByKey.TryGetValue(eventKey, out var item) ? item.Severidad : "info";
    }
}
