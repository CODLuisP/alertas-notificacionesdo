using System.Text;
using Dapper;
using GeocercaNotificationsService.Models;
using GeocercaNotificationsService.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;

namespace GeocercaNotificationsService.Controllers;

/// <summary>
/// Controlador para consultar el historial de alertas de dispositivo registradas en gts.deviceevent
/// (alarm, deviceOverspeed, ignitionOn/Off, deviceOnline/Offline/Unknown y opcionalmente geocercas).
/// </summary>
[Route("api/alertas")]
[ApiController]
[Authorize]
[Produces("application/json")]
public sealed class AlertasController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IConfiguracionRepository _configRepo;
    private readonly ILogger<AlertasController> _logger;

    private static readonly string[] SupportedDeviceEventTypes =
    {
        "alarm",
        "deviceOverspeed",
        "ignitionOn",
        "ignitionOff",
        "deviceOnline",
        "deviceOffline",
        "deviceUnknown",
        "geofenceEnter",
        "geofenceExit"
    };

    public AlertasController(
        IConfiguration configuration,
        IConfiguracionRepository configRepo,
        ILogger<AlertasController> logger)
    {
        _configuration = configuration;
        _configRepo = configRepo;
        _logger = logger;
    }

    /// <summary>
    /// Lista el historial reciente de alertas de una cuenta desde gts.deviceevent.
    /// </summary>
    /// <param name="accountID">Cuenta a consultar (OBLIGATORIO).</param>
    /// <param name="eventKey">Filtro opcional por clave del catálogo (ej: "alarm:sos", "deviceOverspeed", "ignitionOn").</param>
    /// <param name="deviceID">Filtro opcional por placa/vehículo.</param>
    /// <param name="soloActivas">Si es true, devuelve solo los tipos de alerta que la cuenta tiene activos en "Mis notificaciones".</param>
    /// <param name="limit">Cantidad máxima de alertas a devolver (default: 50, máx: 200).</param>
    /// <param name="ct">Token de cancelación.</param>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<DeviceAlertPayload>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAlertas(
        [FromQuery] string accountID,
        [FromQuery] string? eventKey = null,
        [FromQuery] string? deviceID = null,
        [FromQuery] bool soloActivas = false,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountID))
            return BadRequest(new ErrorResponse { Message = "El parámetro 'accountID' es requerido." });

        try
        {
            var account = accountID.Trim();
            if (limit < 1) limit = 50;
            if (limit > 200) limit = 200;

            HashSet<string>? activeKeys = null;
            if (soloActivas)
            {
                var config = await _configRepo.GetConfiguracionAsync(account, ct);
                activeKeys = config.Notificaciones
                    .Select(n => n.EventKey)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (activeKeys.Count == 0)
                    return Ok(Array.Empty<DeviceAlertPayload>());
            }

            var where = new StringBuilder("WHERE accountID = @AccountID");
            var parameters = new DynamicParameters();
            parameters.Add("AccountID", account);

            if (!string.IsNullOrWhiteSpace(eventKey))
            {
                var key = eventKey.Trim();
                if (key.StartsWith("alarm:", StringComparison.OrdinalIgnoreCase))
                {
                    var alarmType = key["alarm:".Length..];
                    where.Append(" AND eventType = 'alarm' AND LOWER(alarmType) = LOWER(@AlarmType)");
                    parameters.Add("AlarmType", alarmType);
                }
                else
                {
                    where.Append(" AND eventType = @EventType");
                    parameters.Add("EventType", key);
                }
            }
            else
            {
                where.Append(" AND eventType IN @EventTypes");
                parameters.Add("EventTypes", SupportedDeviceEventTypes);
            }

            if (!string.IsNullOrWhiteSpace(deviceID))
            {
                where.Append(" AND deviceID = @DeviceID");
                parameters.Add("DeviceID", deviceID.Trim());
            }

            // Si filtramos por soloActivas en memoria (por el caso alarm:sos vs alarm:powercut),
            // traemos un margen ligeramente mayor para completar el límite solicitado.
            var fetchLimit = soloActivas ? Math.Min(limit * 3, 500) : limit;
            parameters.Add("Limit", fetchLimit);

            var sql = $"""
                SELECT id, accountID, deviceID, eventType, serverTime,
                       latitude, longitude, speed, ignition, alarmType,
                       geofenceID, geofenceName, isEnviado
                FROM deviceevent
                {where}
                ORDER BY id DESC
                LIMIT @Limit
                """;

            var connectionString = _configuration.GetConnectionString("GtsDatabase");
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(ct);

            var rows = await connection.QueryAsync<DeviceEvent>(
                new CommandDefinition(sql, parameters, cancellationToken: ct));

            var results = new List<DeviceAlertPayload>(limit);
            foreach (var evt in rows)
            {
                var resolvedKey = AlertEventCatalog.ResolveEventKey(evt.EventType, evt.AlarmType);

                if (activeKeys is not null && !activeKeys.Contains(resolvedKey))
                    continue;

                results.Add(new DeviceAlertPayload
                {
                    Id = evt.Id,
                    AccountID = evt.AccountID,
                    DeviceID = evt.DeviceID,
                    EventType = evt.EventType,
                    AlarmType = evt.AlarmType,
                    EventKey = resolvedKey,
                    Severity = AlertEventCatalog.GetSeverity(resolvedKey),
                    ServerTime = evt.ServerTime,
                    ServerTimeUtc = evt.ServerTimeUtc.ToString("o"),
                    Latitude = evt.Latitude,
                    Longitude = evt.Longitude,
                    Speed = evt.Speed,
                    Ignition = evt.Ignition
                });

                if (results.Count >= limit) break;
            }

            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al consultar historial de alertas para accountID={AccountID}", accountID);
            return StatusCode(500, new ErrorResponse { Message = "Error al obtener el historial de alertas", Error = ex.Message });
        }
    }
}
