using Dapper;
using GeocercaNotificationsService.Models;
using GeocercaNotificationsService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace GeocercaNotificationsService.Controllers;

/// <summary>
/// Controlador para la administración y configuración de notificaciones de WhatsApp por geocerca.
/// Utilizado por el panel de administración web para activar canales, gestionar números de destino
/// y asociar vehículos a números específicos.
/// </summary>
[Route("api/geocercas/{geofenceID:int}/whatsapp")]
[ApiController]
[Authorize]
[Produces("application/json")]
public sealed class GeocercaWhatsAppConfigController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IWhatsAppClient _whatsAppClient;
    private readonly ILogger<GeocercaWhatsAppConfigController> _logger;

    public GeocercaWhatsAppConfigController(
        IConfiguration configuration,
        IWhatsAppClient whatsAppClient,
        ILogger<GeocercaWhatsAppConfigController> logger)
    {
        _configuration = configuration;
        _whatsAppClient = whatsAppClient;
        _logger = logger;
    }

    private MySqlConnection CreateConnection() =>
        new(_configuration.GetConnectionString("GtsDatabase"));

    /// <summary>
    /// Obtiene la configuración completa de WhatsApp para una geocerca, incluyendo sus números destino y vehículos.
    /// </summary>
    /// <param name="geofenceID">ID numérico único de la geocerca (ej. 5).</param>
    /// <response code="200">Retorna la configuración actual, lista de destinatarios con sus carros asignados y vehículos vinculados.</response>
    /// <response code="404">Si la geocerca no existe.</response>
    [HttpGet]
    [ProducesResponseType(typeof(GeocercaWhatsAppDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConfig([FromRoute] int geofenceID)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();

        // 1. Obtener datos de la geocerca
        const string sqlGeo = """
            SELECT id, accountID, geofenceID, nombre,
                   whatsapp_activo AS WhatsappActivo,
                   whatsapp_modo AS WhatsappModo,
                   whatsapp_eventos AS WhatsappEventos
            FROM geocercas
            WHERE geofenceID = @geofenceID
            LIMIT 1
            """;

        var geo = await conn.QueryFirstOrDefaultAsync<GeocercaWhatsAppConfig>(sqlGeo, new { geofenceID });
        if (geo is null)
        {
            return NotFound(new ErrorResponse { Error = $"Geocerca con geofenceID {geofenceID} no encontrada." });
        }

        // 2. Obtener destinatarios configurados
        const string sqlDestinos = """
            SELECT id, id_geocerca AS IdGeocerca, nombre, phone, activo, eventos
            FROM geocercas_whatsapp_destinos
            WHERE id_geocerca = @IdGeocerca
            ORDER BY id ASC
            """;

        var destinos = (await conn.QueryAsync<GeocercaWhatsAppDestino>(sqlDestinos, new { IdGeocerca = geo.Id })).ToList();

        // 3. Obtener asignaciones de vehículos por destinatario
        const string sqlAsignaciones = """
            SELECT dv.id_destino, dv.deviceID
            FROM geocercas_whatsapp_destinos_vehiculos dv
            INNER JOIN geocercas_whatsapp_destinos d ON dv.id_destino = d.id
            WHERE d.id_geocerca = @IdGeocerca
            """;

        var asignaciones = (await conn.QueryAsync<(int IdDestino, string DeviceID)>(sqlAsignaciones, new { IdGeocerca = geo.Id }))
            .GroupBy(x => x.IdDestino)
            .ToDictionary(g => g.Key, g => g.Select(x => x.DeviceID).ToList());

        var destinosDto = destinos.Select(d => new DestinoDetailDto
        {
            Id = d.Id,
            IdGeocerca = d.IdGeocerca,
            Nombre = d.Nombre,
            Phone = d.Phone,
            Activo = d.Activo,
            Eventos = string.IsNullOrWhiteSpace(d.Eventos) ? "ambos" : d.Eventos,
            VehiculosAsignados = asignaciones.TryGetValue(d.Id, out var vehs) ? vehs : new List<string>()
        }).ToList();

        // 4. Obtener vehículos vinculados a la geocerca con su flag notificarWhatsapp
        const string sqlVehiculos = """
            SELECT deviceID, activo, notificarWhatsapp
            FROM geocercas_vehiculos
            WHERE id_geocerca = @IdGeocerca
            ORDER BY deviceID ASC
            """;

        var vehiculos = (await conn.QueryAsync<VehiculoGeocercaDto>(sqlVehiculos, new { IdGeocerca = geo.Id })).ToList();

        var response = new GeocercaWhatsAppDetailDto
        {
            Id = geo.Id,
            AccountID = geo.AccountID,
            GeofenceID = geo.GeofenceID,
            Nombre = geo.Nombre,
            WhatsappActivo = geo.WhatsappActivo,
            WhatsappModo = geo.WhatsappModo,
            WhatsappEventos = geo.WhatsappEventos,
            Destinos = destinosDto,
            Vehiculos = vehiculos
        };

        return Ok(response);
    }

    /// <summary>
    /// Actualiza la configuración general de WhatsApp de una geocerca (activar canal, modo 'todos' o 'seleccionados', tipo de eventos).
    /// </summary>
    /// <param name="geofenceID">ID numérico de la geocerca.</param>
    /// <param name="dto">Nuevos valores de configuración.</param>
    [HttpPut("config")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateConfig(
        [FromRoute] int geofenceID,
        [FromBody] UpdateWhatsAppConfigDto dto)
    {
        var modo = (string.IsNullOrWhiteSpace(dto.WhatsappModo) ? "todos" : dto.WhatsappModo).ToLowerInvariant();
        if (modo != "todos" && modo != "seleccionados")
        {
            return BadRequest(new ErrorResponse { Error = "El campo 'whatsappModo' debe ser 'todos' o 'seleccionados'." });
        }

        var eventos = (string.IsNullOrWhiteSpace(dto.WhatsappEventos) ? "ambos" : dto.WhatsappEventos).ToLowerInvariant();
        if (eventos != "ambos" && eventos != "entrada" && eventos != "salida")
        {
            return BadRequest(new ErrorResponse { Error = "El campo 'whatsappEventos' debe ser 'ambos', 'entrada' o 'salida'." });
        }

        await using var conn = CreateConnection();
        await conn.OpenAsync();

        const string sqlUpdate = """
            UPDATE geocercas
            SET whatsapp_activo = @WhatsappActivo,
                whatsapp_modo = @WhatsappModo,
                whatsapp_eventos = @WhatsappEventos
            WHERE geofenceID = @geofenceID
            """;

        var rows = await conn.ExecuteAsync(sqlUpdate, new
        {
            dto.WhatsappActivo,
            WhatsappModo = modo,
            WhatsappEventos = eventos,
            geofenceID
        });

        if (rows == 0)
        {
            return NotFound(new ErrorResponse { Error = $"Geocerca con geofenceID {geofenceID} no encontrada." });
        }

        return Ok(new { success = true, message = "Configuración de WhatsApp actualizada exitosamente." });
    }

    /// <summary>
    /// Agrega un nuevo número telefónico destinatario a la geocerca, con vehículos asignados opcionales.
    /// </summary>
    [HttpPost("destinos")]
    [ProducesResponseType(typeof(DestinoDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddDestino(
        [FromRoute] int geofenceID,
        [FromBody] CreateDestinoDto dto)
    {
        var normalizedPhone = _whatsAppClient.NormalizePhone(dto.Phone);
        if (string.IsNullOrWhiteSpace(normalizedPhone) || normalizedPhone.Length < 8)
        {
            return BadRequest(new ErrorResponse { Error = $"Número de teléfono inválido: '{dto.Phone}'." });
        }

        await using var conn = CreateConnection();
        await conn.OpenAsync();

        var idGeocerca = await conn.QueryFirstOrDefaultAsync<int?>(
            "SELECT id FROM geocercas WHERE geofenceID = @geofenceID LIMIT 1", new { geofenceID });

        if (!idGeocerca.HasValue)
        {
            return NotFound(new ErrorResponse { Error = $"Geocerca con geofenceID {geofenceID} no encontrada." });
        }

        var eventos = string.IsNullOrWhiteSpace(dto.Eventos) ? "ambos" : dto.Eventos.ToLowerInvariant();
        if (eventos != "ambos" && eventos != "entrada" && eventos != "salida")
        {
            eventos = "ambos";
        }

        // Insertar destino
        const string sqlInsert = """
            INSERT INTO geocercas_whatsapp_destinos (id_geocerca, nombre, phone, activo, eventos)
            VALUES (@IdGeocerca, @Nombre, @Phone, @Activo, @Eventos)
            ON DUPLICATE KEY UPDATE nombre = VALUES(nombre), activo = VALUES(activo), eventos = VALUES(eventos);
            SELECT LAST_INSERT_ID();
            """;

        var destinoId = await conn.ExecuteScalarAsync<int>(sqlInsert, new
        {
            IdGeocerca = idGeocerca.Value,
            dto.Nombre,
            Phone = normalizedPhone,
            dto.Activo,
            Eventos = eventos
        });

        // Insertar vehículos asignados si se especificaron
        var vehiculosAsignados = new List<string>();
        if (dto.Vehiculos != null && dto.Vehiculos.Any())
        {
            foreach (var devId in dto.Vehiculos.Where(v => !string.IsNullOrWhiteSpace(v)))
            {
                const string sqlVeh = """
                    INSERT IGNORE INTO geocercas_whatsapp_destinos_vehiculos (id_destino, deviceID)
                    VALUES (@IdDestino, @DeviceID)
                    """;
                await conn.ExecuteAsync(sqlVeh, new { IdDestino = destinoId, DeviceID = devId.Trim() });
                vehiculosAsignados.Add(devId.Trim());
            }
        }

        return Created($"/api/geocercas/{geofenceID}/whatsapp/destinos/{destinoId}", new DestinoDetailDto
        {
            Id = destinoId,
            IdGeocerca = idGeocerca.Value,
            Nombre = dto.Nombre,
            Phone = normalizedPhone,
            Activo = dto.Activo,
            Eventos = eventos,
            VehiculosAsignados = vehiculosAsignados
        });
    }

    /// <summary>
    /// Modifica los datos o vehículos asignados a un número de destino existente.
    /// </summary>
    [HttpPut("destinos/{destinoId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateDestino(
        [FromRoute] int geofenceID,
        [FromRoute] int destinoId,
        [FromBody] UpdateDestinoDto dto)
    {
        var normalizedPhone = _whatsAppClient.NormalizePhone(dto.Phone);
        if (string.IsNullOrWhiteSpace(normalizedPhone) || normalizedPhone.Length < 8)
        {
            return BadRequest(new ErrorResponse { Error = $"Número de teléfono inválido: '{dto.Phone}'." });
        }

        var eventos = string.IsNullOrWhiteSpace(dto.Eventos) ? "ambos" : dto.Eventos.ToLowerInvariant();
        if (eventos != "ambos" && eventos != "entrada" && eventos != "salida")
        {
            eventos = "ambos";
        }

        await using var conn = CreateConnection();
        await conn.OpenAsync();

        const string sqlUpdate = """
            UPDATE geocercas_whatsapp_destinos
            SET nombre = @Nombre,
                phone = @Phone,
                activo = @Activo,
                eventos = @Eventos
            WHERE id = @DestinoId
            """;

        var rows = await conn.ExecuteAsync(sqlUpdate, new
        {
            dto.Nombre,
            Phone = normalizedPhone,
            dto.Activo,
            Eventos = eventos,
            DestinoId = destinoId
        });

        if (rows == 0)
        {
            return NotFound(new ErrorResponse { Error = $"Destinatario id {destinoId} no encontrado." });
        }

        // Sincronizar vehículos asignados si se envió la lista
        if (dto.Vehiculos != null)
        {
            await conn.ExecuteAsync(
                "DELETE FROM geocercas_whatsapp_destinos_vehiculos WHERE id_destino = @DestinoId",
                new { DestinoId = destinoId });

            foreach (var devId in dto.Vehiculos.Where(v => !string.IsNullOrWhiteSpace(v)))
            {
                await conn.ExecuteAsync("""
                    INSERT IGNORE INTO geocercas_whatsapp_destinos_vehiculos (id_destino, deviceID)
                    VALUES (@DestinoId, @DeviceID)
                    """, new { DestinoId = destinoId, DeviceID = devId.Trim() });
            }
        }

        return Ok(new { success = true, message = "Destinatario actualizado exitosamente." });
    }

    /// <summary>
    /// Elimina un número telefónico de destino de la geocerca.
    /// </summary>
    [HttpDelete("destinos/{destinoId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDestino(
        [FromRoute] int geofenceID,
        [FromRoute] int destinoId)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();

        var rows = await conn.ExecuteAsync(
            "DELETE FROM geocercas_whatsapp_destinos WHERE id = @DestinoId",
            new { DestinoId = destinoId });

        if (rows == 0)
        {
            return NotFound(new ErrorResponse { Error = $"Destinatario id {destinoId} no encontrado." });
        }

        return Ok(new { success = true, message = "Destinatario eliminado exitosamente." });
    }

    /// <summary>
    /// Actualiza el flag notificarWhatsapp para uno o más vehículos vinculados a la geocerca (modo 'seleccionados').
    /// </summary>
    [HttpPut("vehiculos/notificar")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateVehiculosNotificar(
        [FromRoute] int geofenceID,
        [FromBody] UpdateVehiculosNotificarDto dto)
    {
        if (dto.DeviceIds == null || !dto.DeviceIds.Any())
        {
            return BadRequest(new ErrorResponse { Error = "Debe proporcionar al menos un deviceID en 'deviceIds'." });
        }

        await using var conn = CreateConnection();
        await conn.OpenAsync();

        var idGeocerca = await conn.QueryFirstOrDefaultAsync<int?>(
            "SELECT id FROM geocercas WHERE geofenceID = @geofenceID LIMIT 1", new { geofenceID });

        if (!idGeocerca.HasValue)
        {
            return NotFound(new ErrorResponse { Error = $"Geocerca con geofenceID {geofenceID} no encontrada." });
        }

        const string sqlUpdate = """
            UPDATE geocercas_vehiculos
            SET notificarWhatsapp = @NotificarWhatsapp
            WHERE id_geocerca = @IdGeocerca AND deviceID IN @DeviceIds
            """;

        var updated = await conn.ExecuteAsync(sqlUpdate, new
        {
            dto.NotificarWhatsapp,
            IdGeocerca = idGeocerca.Value,
            dto.DeviceIds
        });

        return Ok(new { success = true, message = $"{updated} vehículo(s) actualizado(s) con notificarWhatsapp={dto.NotificarWhatsapp}." });
    }
}
