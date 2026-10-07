using Dapper;
using GeocercaNotificationsService.Handlers;
using GeocercaNotificationsService.Models;
using MySqlConnector;

namespace GeocercaNotificationsService.Services;

/// <summary>
/// Servicio en background que sondea la tabla deviceevent cada PollingIntervalSeconds
/// buscando eventos pendientes (isEnviado = 0) y los despacha al handler correspondiente.
/// </summary>
public sealed class EventPollingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EventPollingService> _logger;

    public EventPollingService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<EventPollingService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollingInterval = _configuration.GetValue("PollingIntervalSeconds", 2);
        _logger.LogInformation(
            "EventPollingService iniciado. Intervalo de sondeo: {Interval}s", pollingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAndProcessAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error no controlado en el ciclo de sondeo");
            }

            await Task.Delay(TimeSpan.FromSeconds(pollingInterval), stoppingToken);
        }

        _logger.LogInformation("EventPollingService detenido.");
    }

    private async Task PollAndProcessAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<EventHandlerRegistry>();
        var supportedTypes = registry.SupportedEventTypes;

        if (supportedTypes.Count == 0)
        {
            _logger.LogDebug("No hay handlers registrados, saltando ciclo");
            return;
        }

        // Construir la cláusula IN dinámicamente según los handlers registrados
        var inClause = string.Join(", ", supportedTypes.Select((_, i) => $"@type{i}"));
        var sql = $"""
            SELECT * FROM deviceevent
            WHERE eventType IN ({inClause}) AND isEnviado = 0
            ORDER BY serverTime ASC
            LIMIT 50
            """;

        var parameters = new DynamicParameters();
        var typeList = supportedTypes.ToList();
        for (int i = 0; i < typeList.Count; i++)
        {
            parameters.Add($"type{i}", typeList[i]);
        }

        var connectionString = _configuration.GetConnectionString("GtsDatabase");
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(ct);

        var events = (await connection.QueryAsync<DeviceEvent>(sql, parameters)).ToList();

        _logger.LogInformation(
            "Ciclo de sondeo: {Count} evento(s) pendiente(s) encontrado(s)", events.Count);

        foreach (var evt in events)
        {
            try
            {
                var handler = registry.GetHandler(evt.EventType);
                if (handler is null)
                {
                    _logger.LogWarning(
                        "Sin handler para eventType='{EventType}', evento id={EventId}",
                        evt.EventType, evt.Id);
                    continue;
                }

                var success = await handler.HandleAsync(evt, ct);

                if (success)
                {
                    await connection.ExecuteAsync(
                        "UPDATE deviceevent SET isEnviado = 1 WHERE id = @Id",
                        new { evt.Id });

                    _logger.LogDebug(
                        "Evento id={EventId} ({EventType}) enviado y marcado como isEnviado=1",
                        evt.Id, evt.EventType);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Error al procesar evento id={EventId} ({EventType}). " +
                    "Se reintentará en el próximo ciclo.",
                    evt.Id, evt.EventType);
                // La fila se queda con isEnviado = 0, se reintentará
            }
        }
    }
}
