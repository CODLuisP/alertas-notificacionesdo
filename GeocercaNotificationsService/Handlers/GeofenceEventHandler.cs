using Dapper;
using GeocercaNotificationsService.Hubs;
using GeocercaNotificationsService.Models;
using GeocercaNotificationsService.Services;
using Microsoft.AspNetCore.SignalR;
using MySqlConnector;

namespace GeocercaNotificationsService.Handlers;

/// <summary>
/// Handler para eventos de geocerca (geofenceEnter / geofenceExit).
/// Para geofenceExit, calcula la duración dentro de la geocerca buscando
/// el geofenceEnter más reciente del mismo deviceID y geofenceID,
/// e inserta una fila en visitas_geocerca.
/// Si la "salida" ocurrió con el vehículo todavía dentro (se lo desvinculó de la geocerca),
/// no se alerta y la visita se guarda marcada con salida_por_desvinculacion = 1.
/// </summary>
public sealed class GeofenceEventHandler : IEventHandler
{
    private readonly IHubContext<NotificationsHub> _hubContext;
    private readonly IConfiguration _configuration;
    private readonly IGeofenceGeometryService _geometry;
    private readonly ILogger<GeofenceEventHandler> _logger;

    public GeofenceEventHandler(
        IHubContext<NotificationsHub> hubContext,
        IConfiguration configuration,
        IGeofenceGeometryService geometry,
        ILogger<GeofenceEventHandler> logger)
    {
        _hubContext = hubContext;
        _configuration = configuration;
        _geometry = geometry;
        _logger = logger;
    }

    public IReadOnlyList<string> SupportedEventTypes { get; } =
        new[] { "geofenceEnter", "geofenceExit" };

    /// <summary>
    /// Datos del geofenceEnter correspondiente a un geofenceExit.
    /// </summary>
    private sealed record EnterEventMatch(
        int EnterId,
        long EnterServerTime,
        double EnterLatitude,
        double EnterLongitude,
        int DurationMinutes);

    public async Task<bool> HandleAsync(DeviceEvent evt, CancellationToken ct)
    {
        EnterEventMatch? enterMatch = null;
        int? durationMinutes = null;
        bool unlinkExit = false;

        bool isExit = string.Equals(evt.EventType, "geofenceExit", StringComparison.OrdinalIgnoreCase);

        if (isExit)
        {
            enterMatch = await FindMatchingEnterAsync(evt);
            durationMinutes = enterMatch?.DurationMinutes;
            unlinkExit = await _geometry.IsUnlinkExitAsync(evt, ct);
        }

        // ── 1. Enviar alerta por SignalR (no para desvinculaciones: el vehículo no salió) ──
        if (!unlinkExit)
        {
            await SendAlertAsync(evt, durationMinutes, ct);
        }

        // ── 2. Insertar en visitas_geocerca (solo exits, independiente del SignalR) ──
        if (isExit)
        {
            try
            {
                await InsertVisitaGeocercaAsync(evt, enterMatch, unlinkExit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error al insertar en visitas_geocerca para evento_salida_id={EventId}.",
                    evt.Id);
            }
        }

        return true;
    }

    private async Task SendAlertAsync(DeviceEvent evt, int? durationMinutes, CancellationToken ct)
    {
        var payload = new GeofenceAlertPayload
        {
            AccountID = evt.AccountID,
            DeviceID = evt.DeviceID,
            GeofenceID = evt.GeofenceID,
            GeofenceName = evt.GeofenceName,
            EventType = evt.EventType,
            ServerTime = evt.ServerTime,
            ServerTimeUtc = evt.ServerTimeUtc.ToString("o"), // ISO 8601
            Latitude = evt.Latitude,
            Longitude = evt.Longitude,
            DurationMinutes = durationMinutes
        };

        await _hubContext.Clients
            .Group(evt.AccountID)
            .SendAsync("GeofenceAlert", payload, ct);
    }

    /// <summary>
    /// Busca el geofenceEnter más reciente para el mismo deviceID + geofenceID,
    /// con serverTime anterior al exit. Retorna null si no lo encuentra.
    /// </summary>
    private async Task<EnterEventMatch?> FindMatchingEnterAsync(DeviceEvent exitEvent)
    {
        const string sql = """
            SELECT * FROM deviceevent
            WHERE deviceID = @DeviceID
              AND geofenceID = @GeofenceID
              AND eventType = 'geofenceEnter'
              AND serverTime < @ExitServerTime
            ORDER BY serverTime DESC
            LIMIT 1
            """;

        try
        {
            var connectionString = _configuration.GetConnectionString("GtsDatabase");
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            var enterEvent = await connection.QueryFirstOrDefaultAsync<DeviceEvent>(sql, new
            {
                exitEvent.DeviceID,
                exitEvent.GeofenceID,
                ExitServerTime = exitEvent.ServerTime
            });

            if (enterEvent is null)
            {
                _logger.LogWarning(
                    "No se encontró geofenceEnter previo para deviceID={DeviceID}, geofenceID={GeofenceID}, exit serverTime={ServerTime}",
                    exitEvent.DeviceID, exitEvent.GeofenceID, exitEvent.ServerTime);
                return null;
            }

            // serverTime es Unix timestamp (segundos). La diferencia da segundos directamente.
            var durationSeconds = exitEvent.ServerTime - enterEvent.ServerTime;
            var durationMinutes = (int)Math.Round(durationSeconds / 60.0);

            return new EnterEventMatch(
                EnterId: enterEvent.Id,
                EnterServerTime: enterEvent.ServerTime,
                EnterLatitude: enterEvent.Latitude,
                EnterLongitude: enterEvent.Longitude,
                DurationMinutes: durationMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error al buscar geofenceEnter para evento id={EventId}", exitEvent.Id);
            return null;
        }
    }

    /// <summary>
    /// Inserta una fila en visitas_geocerca con los datos de la visita completa.
    /// Solo se llama para eventos geofenceExit.
    /// </summary>
    private async Task InsertVisitaGeocercaAsync(DeviceEvent exitEvent, EnterEventMatch? enterMatch, bool unlinkExit)
    {
        const string sql = """
            INSERT INTO visitas_geocerca
                (accountID, deviceID, geofenceID, geofenceName,
                 fecha_entrada, fecha_salida, duracion_minutos,
                 latitud_entrada, longitud_entrada,
                 latitud_salida, longitud_salida,
                 evento_entrada_id, evento_salida_id,
                 entrada_incompleta, salida_por_desvinculacion)
            VALUES
                (@AccountID, @DeviceID, @GeofenceID, @GeofenceName,
                 @FechaEntrada, @FechaSalida, @DuracionMinutos,
                 @LatitudEntrada, @LongitudEntrada,
                 @LatitudSalida, @LongitudSalida,
                 @EventoEntradaId, @EventoSalidaId,
                 @EntradaIncompleta, @SalidaPorDesvinculacion)
            """;

        var connectionString = _configuration.GetConnectionString("GtsDatabase");
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();

        await connection.ExecuteAsync(sql, new
        {
            exitEvent.AccountID,
            exitEvent.DeviceID,
            exitEvent.GeofenceID,
            exitEvent.GeofenceName,
            FechaEntrada = enterMatch is not null
                ? (DateTime?)DateTimeOffset.FromUnixTimeSeconds(enterMatch.EnterServerTime).UtcDateTime
                : null,
            FechaSalida = exitEvent.ServerTimeUtc,
            DuracionMinutos = enterMatch?.DurationMinutes,
            LatitudEntrada = enterMatch?.EnterLatitude,
            LongitudEntrada = enterMatch?.EnterLongitude,
            LatitudSalida = exitEvent.Latitude,
            LongitudSalida = exitEvent.Longitude,
            EventoEntradaId = enterMatch?.EnterId,
            EventoSalidaId = exitEvent.Id,
            EntradaIncompleta = enterMatch is null ? 1 : 0,
            SalidaPorDesvinculacion = unlinkExit ? 1 : 0
        });

        _logger.LogDebug(
            "Visita de geocerca registrada: deviceID={DeviceID}, geofenceID={GeofenceID}, " +
            "evento_salida_id={EventId}, entrada_incompleta={Incompleta}, salida_por_desvinculacion={Desvinculacion}",
            exitEvent.DeviceID, exitEvent.GeofenceID, exitEvent.Id, enterMatch is null, unlinkExit);
    }
}
