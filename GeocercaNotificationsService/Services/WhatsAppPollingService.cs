using Dapper;
using GeocercaNotificationsService.Models;
using MySqlConnector;

namespace GeocercaNotificationsService.Services;

/// <summary>
/// Background Service dedicado al sondeo y despacho de eventos de geocerca por WhatsApp.
/// Corre de manera 100% independiente de EventPollingService (SignalR), garantizando que
/// la latencia de las llamadas HTTP de WhatsApp o posibles fallos no afecten las alertas en tiempo real.
/// Se encuentra completamente resguardado tras WHATSAPP_ENABLED (por defecto false).
/// </summary>
public sealed class WhatsAppPollingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WhatsAppPollingService> _logger;

    public WhatsAppPollingService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<WhatsAppPollingService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = WhatsAppSettings.Load(_configuration);

        if (!settings.Enabled)
        {
            _logger.LogInformation(
                "WhatsAppPollingService inicializado en modo DESACTIVADO (WHATSAPP_ENABLED=false). " +
                "No se realizarán consultas a columnas de WhatsApp en la base de datos.");
        }
        else
        {
            _logger.LogInformation(
                "WhatsAppPollingService INICIADO y ACTIVO. Despachando notificaciones a: {Url}", settings.ApiUrl);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Re-evaluar settings dinámicamente en caso de cambios de entorno
                var currentSettings = WhatsAppSettings.Load(_configuration);

                if (!currentSettings.Enabled)
                {
                    // Cuando está desactivado, espera 5 segundos y vuelve a chequear sin tocar la BD
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                await PollAndProcessWhatsAppAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error no controlado en el ciclo de sondeo de WhatsApp. Se reintentará en el siguiente ciclo.");
            }

            var pollingInterval = _configuration.GetValue("PollingIntervalSeconds", 2);
            await Task.Delay(TimeSpan.FromSeconds(pollingInterval), stoppingToken);
        }

        _logger.LogInformation("WhatsAppPollingService detenido.");
    }

    private async Task PollAndProcessWhatsAppAsync(CancellationToken ct)
    {
        const string sql = """
            SELECT * FROM deviceevent
            WHERE eventType IN ('geofenceEnter', 'geofenceExit')
              AND isEnviadoWhatsapp = 0
              AND intentosWhatsapp < 5
            ORDER BY serverTime ASC
            LIMIT 20
            """;

        var connectionString = _configuration.GetConnectionString("GtsDatabase");
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(ct);

        var pendingEvents = (await connection.QueryAsync<DeviceEvent>(sql)).ToList();

        if (pendingEvents.Count == 0)
        {
            return;
        }

        _logger.LogInformation("WhatsApp ciclo de sondeo: {Count} evento(s) pendiente(s) por despachar", pendingEvents.Count);

        using var scope = _scopeFactory.CreateScope();
        var notificationService = scope.ServiceProvider.GetRequiredService<IWhatsAppNotificationService>();

        foreach (var evt in pendingEvents)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await notificationService.ProcessEventAsync(evt, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Error al procesar notificación de WhatsApp para evento id={EventId} ({EventType}). Se reintentará en el siguiente ciclo.",
                    evt.Id, evt.EventType);
            }
        }
    }
}
