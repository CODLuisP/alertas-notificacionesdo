using GeocercaNotificationsService.Models;

namespace GeocercaNotificationsService.Repositories;

/// <summary>
/// Repositorio SQLite embebido para la configuración del sistema y preferencias de alertas por cuenta.
/// </summary>
public interface IConfiguracionRepository
{
    /// <summary>
    /// Obtiene toda la configuración consolidada de una cuenta (o valores por defecto si es nueva).
    /// </summary>
    Task<ConfiguracionCuentaResponse> GetConfiguracionAsync(string accountID, CancellationToken ct = default);

    /// <summary>
    /// Devuelve la configuración de una alerta específica si la cuenta la tiene activada,
    /// o null si la cuenta no la ha agregado / la tiene apagada.
    /// Usa caché en memoria para consultas en microsegundos durante el polling.
    /// </summary>
    Task<AlertaNotificacionConfigDto?> GetActiveAlertConfigAsync(string accountID, string eventKey, CancellationToken ct = default);

    /// <summary>
    /// Guarda la pestaña "Preferencias" (tipo de mapa, placas, agrupar, seguir unidad).
    /// </summary>
    Task<PreferenciasVisualizacionDto> SavePreferenciasAsync(string accountID, PreferenciasVisualizacionDto dto, CancellationToken ct = default);

    /// <summary>
    /// Guarda la pestaña "Alertas y sonidos" (sonido global, no molestar y lista activa de notificaciones).
    /// </summary>
    Task<GuardarAlertasConfigRequest> SaveAlertasAsync(string accountID, GuardarAlertasConfigRequest request, CancellationToken ct = default);

    /// <summary>
    /// Guarda la pestaña "Límites de velocidad" (modo flota/vehículo, límite global y límites por placa).
    /// </summary>
    Task<LimitesVelocidadDto> SaveLimitesVelocidadAsync(string accountID, LimitesVelocidadDto dto, CancellationToken ct = default);

    /// <summary>
    /// Guarda o actualiza los íconos asignados a una o varias unidades (pestaña "Íconos").
    /// </summary>
    Task<Dictionary<string, string>> SaveIconosAsync(string accountID, GuardarIconosRequest request, CancellationToken ct = default);

    /// <summary>
    /// Elimina todos los íconos personalizados de una cuenta ("Restablecer todas a Auto").
    /// </summary>
    Task ResetIconosAsync(string accountID, CancellationToken ct = default);
}
