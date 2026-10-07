using System.Text.Json.Serialization;

namespace GeocercaNotificationsService.Models;

/// <summary>
/// Payload enviado a la API de WhatsApp POST https://do.velsat.pe:8443/whatsapp/api/send/single
/// </summary>
public sealed class WhatsAppSendRequest
{
    [JsonPropertyName("phone")]
    public string Phone { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "texto";

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Resultado de la llamada HTTP a la API de WhatsApp.
/// </summary>
public sealed class WhatsAppSendResult
{
    public bool IsSuccess { get; set; }
    public int StatusCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResponseBody { get; set; }
    public string NormalizedPhone { get; set; } = string.Empty;

    public static WhatsAppSendResult Success(string phone, int statusCode, string? responseBody) =>
        new()
        {
            IsSuccess = true,
            StatusCode = statusCode,
            NormalizedPhone = phone,
            ResponseBody = responseBody
        };

    public static WhatsAppSendResult Failure(string phone, int statusCode, string errorMessage, string? responseBody = null) =>
        new()
        {
            IsSuccess = false,
            StatusCode = statusCode,
            NormalizedPhone = phone,
            ErrorMessage = errorMessage,
            ResponseBody = responseBody
        };
}

/// <summary>
/// Mapea la configuración de WhatsApp de una geocerca en la tabla `geocercas`.
/// </summary>
public sealed class GeocercaWhatsAppConfig
{
    public int Id { get; set; }
    public string AccountID { get; set; } = string.Empty;
    public int GeofenceID { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public bool Activo { get; set; }

    /// <summary>
    /// 1 = Notificaciones de WhatsApp habilitadas para esta geocerca, 0 = Inactivo.
    /// </summary>
    public bool WhatsappActivo { get; set; }

    /// <summary>
    /// Modo de notificación: 'todos' o 'seleccionados'.
    /// </summary>
    public string WhatsappModo { get; set; } = "todos";

    /// <summary>
    /// Tipo de eventos a notificar: 'ambos', 'entrada', 'salida'.
    /// </summary>
    public string WhatsappEventos { get; set; } = "ambos";
}

/// <summary>
/// Destinatario configurado en la tabla `geocercas_whatsapp_destinos`.
/// </summary>
public sealed class GeocercaWhatsAppDestino
{
    public int Id { get; set; }
    public int IdGeocerca { get; set; }
    public string? Nombre { get; set; }
    public string Phone { get; set; } = string.Empty;
    public bool Activo { get; set; }

    /// <summary>
    /// Tipo de eventos que recibe este número: 'ambos', 'entrada', 'salida'.
    /// </summary>
    public string Eventos { get; set; } = "ambos";
}

/// <summary>
/// Solicitud de prueba manual de envío de WhatsApp.
/// </summary>
public sealed class TestWhatsAppRequest
{
    /// <summary>
    /// Número de teléfono con código de país (ej. '51987654321' o '+51 987 654 321').
    /// </summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>
    /// Mensaje de texto a enviar (opcional: si se deja en blanco, se envía un mensaje de alerta de ejemplo).
    /// </summary>
    public string? Message { get; set; }
}

/// <summary>
/// Mapea la asociación específica entre un destinatario de WhatsApp y los vehículos autorizados.
/// Si un destinatario no tiene filas aquí, recibe alertas de todos los vehículos de la geocerca.
/// </summary>
public sealed class GeocercaWhatsAppDestinoVehiculo
{
    public int Id { get; set; }
    public int IdDestino { get; set; }
    public string DeviceID { get; set; } = string.Empty;
    public DateTime FechaAsignacion { get; set; }
}

public sealed class GeocercaWhatsAppDetailDto
{
    public int Id { get; set; }
    public string AccountID { get; set; } = string.Empty;
    public int GeofenceID { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public bool WhatsappActivo { get; set; }
    public string WhatsappModo { get; set; } = "todos";
    public string WhatsappEventos { get; set; } = "ambos";
    public List<DestinoDetailDto> Destinos { get; set; } = new();
    public List<VehiculoGeocercaDto> Vehiculos { get; set; } = new();
}

public sealed class DestinoDetailDto
{
    public int Id { get; set; }
    public int IdGeocerca { get; set; }
    public string? Nombre { get; set; }
    public string Phone { get; set; } = string.Empty;
    public bool Activo { get; set; }

    /// <summary>
    /// Tipo de eventos para este destino: 'ambos', 'entrada', 'salida'.
    /// </summary>
    public string Eventos { get; set; } = "ambos";

    public List<string> VehiculosAsignados { get; set; } = new();
}

public sealed class VehiculoGeocercaDto
{
    public string DeviceID { get; set; } = string.Empty;
    public bool Activo { get; set; }
    public bool NotificarWhatsapp { get; set; }
}

public sealed class UpdateWhatsAppConfigDto
{
    public bool WhatsappActivo { get; set; }
    public string WhatsappModo { get; set; } = "todos"; // 'todos' o 'seleccionados'
    public string WhatsappEventos { get; set; } = "ambos"; // 'ambos', 'entrada', 'salida'
}

public sealed class CreateDestinoDto
{
    public string? Nombre { get; set; }
    public string Phone { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;

    /// <summary>
    /// Tipo de eventos a recibir: 'ambos', 'entrada', 'salida' (por defecto: 'ambos').
    /// </summary>
    public string Eventos { get; set; } = "ambos";

    public List<string>? Vehiculos { get; set; }
}

public sealed class UpdateDestinoDto
{
    public string? Nombre { get; set; }
    public string Phone { get; set; } = string.Empty;
    public bool Activo { get; set; } = true;

    /// <summary>
    /// Tipo de eventos a recibir: 'ambos', 'entrada', 'salida'.
    /// </summary>
    public string Eventos { get; set; } = "ambos";

    public List<string>? Vehiculos { get; set; }
}

public sealed class UpdateVehiculosNotificarDto
{
    public List<string> DeviceIds { get; set; } = new();
    public bool NotificarWhatsapp { get; set; }
}
