using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GeocercaNotificationsService.Models;

namespace GeocercaNotificationsService.Services;

/// <summary>
/// Sincroniza attributes.speedLimit (en nudos: km/h ÷ 1.852) en la API de Traccar
/// filtrando estrictamente por attributes.accountID para no tocar unidades de otras cuentas.
/// </summary>
public sealed class TraccarSpeedLimitService : ITraccarSpeedLimitService
{
    private const double KnotsFactor = 1.852;

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TraccarSpeedLimitService> _logger;

    public TraccarSpeedLimitService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<TraccarSpeedLimitService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<TraccarSpeedLimitSyncResult> SyncAccountSpeedLimitsAsync(
        string accountID,
        LimitesVelocidadDto limites,
        CancellationToken ct = default)
    {
        var settings = TraccarSettings.Load(_configuration);
        if (!settings.IsConfigured)
        {
            _logger.LogDebug("Sincronización de speedLimit con Traccar omitida: credenciales Traccar__* no configuradas.");
            return new TraccarSpeedLimitSyncResult
            {
                Habilitado = false,
                Mensaje = "Sincronización con Traccar deshabilitada (faltan variables Traccar__BaseUrl/Username/Password)."
            };
        }

        var targetAccount = accountID.Trim();
        var authHeader = BuildBasicAuthHeader(settings.Username, settings.Password);

        // 1. Obtener el listado de dispositivos de Traccar y filtrar SOLO los de esta cuenta
        JsonArray? allDevices;
        try
        {
            using var getReq = new HttpRequestMessage(HttpMethod.Get, $"{settings.BaseUrl}/api/devices");
            getReq.Headers.Authorization = authHeader;
            getReq.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

            using var resp = await _httpClient.SendAsync(getReq, cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(cts.Token);
                _logger.LogError(
                    "Error al consultar /api/devices en Traccar ({Status}): {Body}",
                    (int)resp.StatusCode, body);
                return new TraccarSpeedLimitSyncResult
                {
                    Habilitado = true,
                    Errores = 1,
                    Mensaje = $"Error al consultar Traccar ({(int)resp.StatusCode})."
                };
            }

            var json = await resp.Content.ReadAsStringAsync(cts.Token);
            allDevices = JsonNode.Parse(json) as JsonArray;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Excepción al consultar /api/devices en Traccar para accountID={AccountID}", targetAccount);
            return new TraccarSpeedLimitSyncResult
            {
                Habilitado = true,
                Errores = 1,
                Mensaje = $"No se pudo conectar con Traccar: {ex.Message}"
            };
        }

        if (allDevices is null)
        {
            return new TraccarSpeedLimitSyncResult
            {
                Habilitado = true,
                Mensaje = "Respuesta vacía desde Traccar."
            };
        }

        // 2. Filtrar estrictamente por attributes.accountID == targetAccount
        var accountDevices = new List<JsonObject>();
        foreach (var node in allDevices)
        {
            if (node is not JsonObject devObj) continue;
            var attrs = devObj["attributes"] as JsonObject;
            var devAccount = attrs?["accountID"]?.GetValue<string>()?.Trim();
            if (string.Equals(devAccount, targetAccount, StringComparison.OrdinalIgnoreCase))
            {
                accountDevices.Add(devObj);
            }
        }

        if (accountDevices.Count == 0)
        {
            _logger.LogWarning(
                "No se encontraron dispositivos en Traccar con attributes.accountID='{AccountID}'.",
                targetAccount);
            return new TraccarSpeedLimitSyncResult
            {
                Habilitado = true,
                TotalUnidadesCuenta = 0,
                Mensaje = $"No se encontraron unidades en Traccar para la cuenta '{targetAccount}'."
            };
        }

        // 3. Actualizar en lotes controlados solo las unidades cuyo speedLimit haya cambiado
        var isVehicleMode = string.Equals(limites.Modo, "vehiculo", StringComparison.OrdinalIgnoreCase);
        var porVehiculo = limites.PorVehiculo is not null
            ? new Dictionary<string, int>(limites.PorVehiculo, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int actualizadas = 0;
        int sinCambios = 0;
        int errores = 0;

        using var semaphore = new SemaphoreSlim(settings.MaxConcurrency);
        var tasks = accountDevices.Select(async devObj =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var deviceIdNum = devObj["id"]?.GetValue<int>() ?? 0;
                if (deviceIdNum <= 0)
                {
                    Interlocked.Increment(ref errores);
                    return;
                }

                var attrs = devObj["attributes"] as JsonObject;
                if (attrs is null)
                {
                    attrs = new JsonObject();
                    devObj["attributes"] = attrs;
                }

                // Placa / identificador del vehículo en el sistema
                var placa = attrs["deviceID"]?.GetValue<string>()?.Trim()
                            ?? devObj["name"]?.GetValue<string>()?.Trim()
                            ?? string.Empty;

                var targetKmh = isVehicleMode && !string.IsNullOrWhiteSpace(placa) && porVehiculo.TryGetValue(placa, out var customKmh)
                    ? customKmh
                    : limites.LimiteFlotaKmh;

                var targetKnots = Math.Round(targetKmh / KnotsFactor, 2, MidpointRounding.AwayFromZero);

                // Si ya tiene exactamente el mismo speedLimit, evitamos un PUT innecesario
                if (TryGetExistingSpeedLimit(attrs, out var currentKnots) &&
                    Math.Abs(currentKnots - targetKnots) < 0.01)
                {
                    Interlocked.Increment(ref sinCambios);
                    return;
                }

                attrs["speedLimit"] = targetKnots;

                var payloadJson = devObj.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
                using var putReq = new HttpRequestMessage(HttpMethod.Put, $"{settings.BaseUrl}/api/devices/{deviceIdNum}")
                {
                    Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
                };
                putReq.Headers.Authorization = authHeader;

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(settings.TimeoutSeconds));

                using var putResp = await _httpClient.SendAsync(putReq, cts.Token);
                if (putResp.IsSuccessStatusCode)
                {
                    Interlocked.Increment(ref actualizadas);
                }
                else
                {
                    var errBody = await putResp.Content.ReadAsStringAsync(cts.Token);
                    _logger.LogWarning(
                        "Error al actualizar speedLimit en Traccar para device id={TraccarId} ({Placa}): {Status} {Body}",
                        deviceIdNum, placa, (int)putResp.StatusCode, errBody);
                    Interlocked.Increment(ref errores);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Excepción al actualizar dispositivo en Traccar para accountID={AccountID}", targetAccount);
                Interlocked.Increment(ref errores);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        _logger.LogInformation(
            "Sincronización speedLimit Traccar completada para accountID='{AccountID}': total={Total}, actualizadas={Updated}, sinCambios={Unchanged}, errores={Errors}",
            targetAccount, accountDevices.Count, actualizadas, sinCambios, errores);

        return new TraccarSpeedLimitSyncResult
        {
            Habilitado = true,
            TotalUnidadesCuenta = accountDevices.Count,
            Actualizadas = actualizadas,
            SinCambios = sinCambios,
            Errores = errores,
            Mensaje = errores == 0
                ? $"Sincronizado con Traccar ({actualizadas} actualizadas, {sinCambios} sin cambios)."
                : $"Sincronizado parcialmente ({actualizadas} actualizadas, {sinCambios} sin cambios, {errores} errores)."
        };
    }

    private static AuthenticationHeaderValue BuildBasicAuthHeader(string username, string password)
    {
        var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        return new AuthenticationHeaderValue("Basic", raw);
    }

    private static bool TryGetExistingSpeedLimit(JsonObject attrs, out double value)
    {
        value = 0;
        var node = attrs["speedLimit"];
        if (node is null) return false;

        try
        {
            if (node is JsonValue jv)
            {
                if (jv.TryGetValue<double>(out var d))
                {
                    value = d;
                    return true;
                }
                if (jv.TryGetValue<string>(out var s) &&
                    double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    value = parsed;
                    return true;
                }
            }
        }
        catch
        {
            // Ignorar valores malformados y sobrescribir con el valor correcto
        }

        return false;
    }
}
