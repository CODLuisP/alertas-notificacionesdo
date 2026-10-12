using System.Globalization;
using Dapper;
using GeocercaNotificationsService.Models;
using MySqlConnector;

namespace GeocercaNotificationsService.Services;

/// <summary>
/// Orquestador para la evaluación de reglas y despacho de notificaciones de geocerca por WhatsApp.
/// </summary>
public sealed class WhatsAppNotificationService : IWhatsAppNotificationService
{
    private static readonly TimeZoneInfo LimaTimeZone = ResolveLimaTimeZone();

    private readonly IWhatsAppClient _whatsAppClient;
    private readonly IConfiguration _configuration;
    private readonly IGeofenceGeometryService _geometry;
    private readonly WhatsAppSettings _settings;
    private readonly ILogger<WhatsAppNotificationService> _logger;

    public WhatsAppNotificationService(
        IWhatsAppClient whatsAppClient,
        IConfiguration configuration,
        IGeofenceGeometryService geometry,
        ILogger<WhatsAppNotificationService> logger)
    {
        _whatsAppClient = whatsAppClient;
        _configuration = configuration;
        _geometry = geometry;
        _settings = WhatsAppSettings.Load(configuration);
        _logger = logger;
    }

    private static TimeZoneInfo ResolveLimaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Lima");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("SA Pacific Standard Time");
        }
    }

    /// <inheritdoc />
    public async Task<bool> ProcessEventAsync(DeviceEvent evt, CancellationToken ct)
    {
        if (evt.GeofenceID is null)
        {
            _logger.LogDebug("Evento id={EventId} no tiene geofenceID asignado. Descartando para WhatsApp.", evt.Id);
            await MarkEventProcessedAsync(evt.Id, 1);
            return true;
        }

        // Una "salida" con el vehículo todavía adentro es una desvinculación, no una salida real:
        // no se avisa por WhatsApp (se marca como descartado).
        if (string.Equals(evt.EventType, "geofenceExit", StringComparison.OrdinalIgnoreCase) &&
            await _geometry.IsUnlinkExitAsync(evt, ct))
        {
            await MarkEventProcessedAsync(evt.Id, 1);
            return true;
        }

        var connectionString = _configuration.GetConnectionString("GtsDatabase");
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(ct);

        // ── 1. Buscar geocerca por geofenceID y validar whatsapp_activo ──────
        const string sqlGeocerca = """
            SELECT id, accountID, geofenceID, nombre, activo,
                   whatsapp_activo AS WhatsappActivo,
                   whatsapp_modo AS WhatsappModo,
                   whatsapp_eventos AS WhatsappEventos
            FROM geocercas
            WHERE geofenceID = @GeofenceID
            LIMIT 1
            """;

        var geocerca = await connection.QueryFirstOrDefaultAsync<GeocercaWhatsAppConfig>(
            sqlGeocerca, new { GeofenceID = evt.GeofenceID.Value });

        if (geocerca is null || !geocerca.Activo || !geocerca.WhatsappActivo)
        {
            _logger.LogDebug(
                "Geocerca geofenceID={GeofenceID} no encontrada o tiene whatsapp_activo=0. Evento id={EventId} marcado.",
                evt.GeofenceID, evt.Id);
            await MarkEventProcessedAsync(evt.Id, 1);
            return true;
        }

        var isEnter = string.Equals(evt.EventType, "geofenceEnter", StringComparison.OrdinalIgnoreCase);
        var isExit = string.Equals(evt.EventType, "geofenceExit", StringComparison.OrdinalIgnoreCase);

        // ── 2. Obtener destinos activos que coincidan con el tipo de evento y el vehículo ──
        // Cada contacto define individualmente:
        // a) Tipo de evento que le interesa: 'ambos', 'entrada' o 'salida'.
        // b) Vehículos que supervisa: todos (si no tiene filas en destinos_vehiculos) o específicos.
        const string sqlDestinos = """
            SELECT d.id, d.id_geocerca, d.nombre, d.phone, d.activo, d.eventos
            FROM geocercas_whatsapp_destinos d
            WHERE d.id_geocerca = @IdGeocerca
              AND d.activo = 1
              AND (
                  d.eventos = 'ambos'
                  OR (d.eventos = 'entrada' AND @IsEnter = 1)
                  OR (d.eventos = 'salida' AND @IsExit = 1)
              )
              AND (
                  NOT EXISTS (
                      SELECT 1 FROM geocercas_whatsapp_destinos_vehiculos dv
                      WHERE dv.id_destino = d.id
                  )
                  OR EXISTS (
                      SELECT 1 FROM geocercas_whatsapp_destinos_vehiculos dv
                      WHERE dv.id_destino = d.id AND dv.deviceID = @DeviceID
                  )
              )
            """;

        var destinos = (await connection.QueryAsync<GeocercaWhatsAppDestino>(
            sqlDestinos, new
            {
                IdGeocerca = geocerca.Id,
                IsEnter = isEnter ? 1 : 0,
                IsExit = isExit ? 1 : 0,
                evt.DeviceID
            })).ToList();

        if (destinos.Count == 0)
        {
            _logger.LogInformation(
                "Geocerca id={GeocercaId} ('{Nombre}') no tiene números destino interesados en el vehículo '{DeviceID}'. Evento id={EventId} marcado como completado.",
                geocerca.Id, geocerca.Nombre, evt.DeviceID, evt.Id);
            await MarkEventProcessedAsync(evt.Id, 1);
            return true;
        }

        // ── 5. Revisar qué números ya recibieron este evento (reintentos) ────
        const string sqlEnviosPrevios = """
            SELECT phone FROM evento_whatsapp_envios
            WHERE id_evento = @EventId
            """;

        var enviosPrevios = (await connection.QueryAsync<string>(
            sqlEnviosPrevios, new { EventId = evt.Id })).Select(_whatsAppClient.NormalizePhone).ToHashSet();

        // Destinos pendientes para esta ronda
        var destinosPendientes = destinos
            .Where(d => !enviosPrevios.Contains(_whatsAppClient.NormalizePhone(d.Phone)))
            .ToList();

        if (destinosPendientes.Count == 0)
        {
            // Todos los números ya habían recibido el mensaje exitosamente
            await connection.ExecuteAsync("""
                UPDATE deviceevent
                SET isEnviadoWhatsapp = 1,
                    fechaEnvioWhatsapp = NOW()
                WHERE id = @EventId
                """, new { EventId = evt.Id });
            return true;
        }

        // Construir el texto del mensaje
        var geofenceName = !string.IsNullOrWhiteSpace(evt.GeofenceName) ? evt.GeofenceName : geocerca.Nombre;
        var messageText = await BuildMessageTextAsync(evt, geofenceName, ct);

        // Despachar a cada destino pendiente con pausa entre envíos
        foreach (var destino in destinosPendientes)
        {
            var normalized = _whatsAppClient.NormalizePhone(destino.Phone);
            var result = await _whatsAppClient.SendTextMessageAsync(normalized, messageText, ct);

            if (result.IsSuccess)
            {
                // Registrar envío parcial exitoso
                const string sqlInsertEnvio = """
                    INSERT INTO evento_whatsapp_envios (id_evento, phone, enviado_en)
                    VALUES (@EventId, @Phone, NOW())
                    ON DUPLICATE KEY UPDATE enviado_en = VALUES(enviado_en)
                    """;

                await connection.ExecuteAsync(sqlInsertEnvio, new
                {
                    EventId = evt.Id,
                    Phone = normalized
                });

                enviosPrevios.Add(normalized);
            }
            else
            {
                _logger.LogWarning(
                    "Fallo de envío WhatsApp al número {Phone} para evento id={EventId}: {Error}",
                    destino.Phone, evt.Id, result.ErrorMessage);
            }

            // Pausa entre mensajes para no saturar la API
            if (_settings.DelayBetweenSendsMs > 0)
            {
                await Task.Delay(_settings.DelayBetweenSendsMs, ct);
            }
        }

        // ── 6. Evaluar si todos los destinos fueron cubiertos ────────────────
        var todosCubiertos = destinos.All(d => enviosPrevios.Contains(_whatsAppClient.NormalizePhone(d.Phone)));

        if (todosCubiertos)
        {
            await connection.ExecuteAsync("""
                UPDATE deviceevent
                SET isEnviadoWhatsapp = 1,
                    fechaEnvioWhatsapp = NOW()
                WHERE id = @EventId
                """, new { EventId = evt.Id });

            _logger.LogInformation(
                "Evento id={EventId} despachado exitosamente por WhatsApp a {Count} destino(s).",
                evt.Id, destinos.Count);
            return true;
        }

        // Si falló algún número, incrementar intentosWhatsapp
        var nuevoIntento = evt.IntentosWhatsapp + 1;
        var maxAlcanzado = nuevoIntento >= 5;

        await connection.ExecuteAsync("""
            UPDATE deviceevent
            SET intentosWhatsapp = @NuevoIntento,
                isEnviadoWhatsapp = CASE WHEN @MaxAlcanzado THEN 2 ELSE 0 END
            WHERE id = @EventId
            """, new
        {
            NuevoIntento = nuevoIntento,
            MaxAlcanzado = maxAlcanzado,
            EventId = evt.Id
        });

        if (maxAlcanzado)
        {
            _logger.LogError(
                "Evento id={EventId} alcanzó el máximo de 5 intentos fallidos de WhatsApp. Se marca como isEnviadoWhatsapp=2 para evitar bucle continuo.",
                evt.Id);
        }
        else
        {
            _logger.LogWarning(
                "Evento id={EventId} tuvo fallos en envío de WhatsApp (intento {Intento}/5). Se reintentará en el próximo ciclo.",
                evt.Id, nuevoIntento);
        }

        return false;
    }

    /// <inheritdoc />
    public async Task<string> BuildMessageTextAsync(DeviceEvent evt, string geofenceName, CancellationToken ct)
    {
        // 1. Convertir Unix timestamp a hora local America/Lima (UTC-5)
        var dtoUtc = DateTimeOffset.FromUnixTimeSeconds(evt.ServerTime);
        var limaTime = TimeZoneInfo.ConvertTime(dtoUtc, LimaTimeZone);
        var horaStr = limaTime.ToString("dd/MM/yyyy HH:mm");

        // 2. Coordenadas con punto decimal invariant
        var latStr = evt.Latitude.ToString(CultureInfo.InvariantCulture);
        var lonStr = evt.Longitude.ToString(CultureInfo.InvariantCulture);
        var mapsUrl = $"https://maps.google.com/?q={latStr},{lonStr}";

        var isExit = string.Equals(evt.EventType, "geofenceExit", StringComparison.OrdinalIgnoreCase);

        if (!isExit)
        {
            // Formato Entrada
            return $"""
                🟢 *ENTRADA a geocerca*
                Vehículo: {evt.DeviceID}
                Geocerca: {geofenceName}
                Hora: {horaStr}
                📍 {mapsUrl}
                """;
        }

        // Formato Salida: calcular duración si está disponible
        int? durationMinutes = await ResolveDurationMinutesAsync(evt, ct);

        var lineaDuracion = durationMinutes.HasValue && durationMinutes.Value > 0
            ? $"\nDuración: {durationMinutes.Value} min"
            : string.Empty;

        return $"""
            🔴 *SALIDA de geocerca*
            Vehículo: {evt.DeviceID}
            Geocerca: {geofenceName}
            Hora: {horaStr}{lineaDuracion}
            📍 {mapsUrl}
            """;
    }

    private async Task<int?> ResolveDurationMinutesAsync(DeviceEvent exitEvent, CancellationToken ct)
    {
        try
        {
            var connectionString = _configuration.GetConnectionString("GtsDatabase");
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync(ct);

            // Intentar primero obtener la duración ya calculada en visitas_geocerca
            const string sqlVisita = """
                SELECT duracion_minutos FROM visitas_geocerca
                WHERE evento_salida_id = @EventoId
                LIMIT 1
                """;

            var duracionVisita = await connection.QueryFirstOrDefaultAsync<int?>(sqlVisita, new { EventoId = exitEvent.Id });
            if (duracionVisita.HasValue && duracionVisita.Value > 0)
            {
                return duracionVisita.Value;
            }

            // Fallback: calcular contra el último geofenceEnter previo
            const string sqlEnter = """
                SELECT serverTime FROM deviceevent
                WHERE deviceID = @DeviceID
                  AND geofenceID = @GeofenceID
                  AND eventType = 'geofenceEnter'
                  AND serverTime < @ExitServerTime
                ORDER BY serverTime DESC
                LIMIT 1
                """;

            var enterServerTime = await connection.QueryFirstOrDefaultAsync<long?>(sqlEnter, new
            {
                exitEvent.DeviceID,
                exitEvent.GeofenceID,
                ExitServerTime = exitEvent.ServerTime
            });

            if (enterServerTime.HasValue)
            {
                var diffSec = exitEvent.ServerTime - enterServerTime.Value;
                if (diffSec > 0)
                {
                    return (int)Math.Round(diffSec / 60.0);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo calcular la duración para salida de geocerca evento id={EventId}", exitEvent.Id);
        }

        return null;
    }

    private async Task MarkEventProcessedAsync(int eventId, int status)
    {
        try
        {
            var connectionString = _configuration.GetConnectionString("GtsDatabase");
            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();

            await connection.ExecuteAsync("""
                UPDATE deviceevent
                SET isEnviadoWhatsapp = @Status,
                    fechaEnvioWhatsapp = NOW()
                WHERE id = @EventId
                """, new { EventId = eventId, Status = status });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al marcar evento id={EventId} con isEnviadoWhatsapp={Status}", eventId, status);
        }
    }
}
