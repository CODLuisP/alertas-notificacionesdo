using GeocercaNotificationsService.Models;
using GeocercaNotificationsService.Repositories;
using GeocercaNotificationsService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeocercaNotificationsService.Controllers;

/// <summary>
/// Controlador para la configuración del sistema y preferencias por cuenta (SQLite embebido).
/// Gestiona: Preferencias de mapa, Alertas y sonidos, Límites de velocidad e Íconos de unidades.
/// </summary>
[Route("api/configuracion")]
[ApiController]
[Authorize]
[Produces("application/json")]
public sealed class ConfiguracionController : ControllerBase
{
    private readonly IConfiguracionRepository _repository;
    private readonly ITraccarSpeedLimitService _traccarSpeedLimit;
    private readonly ILogger<ConfiguracionController> _logger;

    public ConfiguracionController(
        IConfiguracionRepository repository,
        ITraccarSpeedLimitService traccarSpeedLimit,
        ILogger<ConfiguracionController> logger)
    {
        _repository = repository;
        _traccarSpeedLimit = traccarSpeedLimit;
        _logger = logger;
    }

    /// <summary>
    /// Obtiene toda la configuración consolidada de una cuenta (preferencias, alertas y sonidos,
    /// límites de velocidad e íconos de unidades).
    /// Si la cuenta es nueva, devuelve los valores por defecto con todas las alertas desactivadas.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ConfiguracionCuentaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetConfiguracion(
        [FromQuery] string accountID,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountID))
            return BadRequest(new ErrorResponse { Message = "El parámetro 'accountID' es requerido." });

        try
        {
            var config = await _repository.GetConfiguracionAsync(accountID, ct);
            return Ok(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener configuración para accountID={AccountID}", accountID);
            return StatusCode(500, new ErrorResponse { Message = "Error al obtener la configuración", Error = ex.Message });
        }
    }

    /// <summary>
    /// Devuelve el catálogo oficial de alertas soportadas para el botón "+ Agregar notificación".
    /// </summary>
    [HttpGet("alertas/catalogo")]
    [ProducesResponseType(typeof(IReadOnlyList<CatalogoAlertaItem>), StatusCodes.Status200OK)]
    public IActionResult GetCatalogoAlertas()
    {
        return Ok(AlertEventCatalog.Items);
    }

    /// <summary>
    /// Guarda la pestaña "Preferencias" (tipo de mapa, mostrar placas, agrupar unidades, seguir unidad).
    /// </summary>
    [HttpPut("preferencias")]
    [ProducesResponseType(typeof(PreferenciasVisualizacionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SavePreferencias(
        [FromQuery] string accountID,
        [FromBody] PreferenciasVisualizacionDto body,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountID))
            return BadRequest(new ErrorResponse { Message = "El parámetro 'accountID' es requerido." });

        try
        {
            var saved = await _repository.SavePreferenciasAsync(accountID, body, ct);
            return Ok(saved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al guardar preferencias para accountID={AccountID}", accountID);
            return StatusCode(500, new ErrorResponse { Message = "Error al guardar las preferencias", Error = ex.Message });
        }
    }

    /// <summary>
    /// Guarda la pestaña "Alertas y sonidos" (sonido global, horario No molestar y lista de notificaciones activas).
    /// Solo las alertas incluidas en "notificaciones" se enviarán en tiempo real por SignalR a esta cuenta.
    /// </summary>
    [HttpPut("alertas")]
    [ProducesResponseType(typeof(GuardarAlertasConfigRequest), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SaveAlertas(
        [FromQuery] string accountID,
        [FromBody] GuardarAlertasConfigRequest body,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountID))
            return BadRequest(new ErrorResponse { Message = "El parámetro 'accountID' es requerido." });

        if (body.Notificaciones is not null)
        {
            var invalidKeys = body.Notificaciones
                .Where(n => !AlertEventCatalog.IsValidEventKey(n.EventKey))
                .Select(n => n.EventKey)
                .Distinct()
                .ToList();

            if (invalidKeys.Count > 0)
            {
                return BadRequest(new ErrorResponse
                {
                    Message = $"Las siguientes claves de alerta no son válidas: {string.Join(", ", invalidKeys)}. " +
                              "Consulte GET /api/configuracion/alertas/catalogo para ver las claves soportadas."
                });
            }
        }

        try
        {
            var saved = await _repository.SaveAlertasAsync(accountID, body, ct);
            return Ok(saved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al guardar configuración de alertas para accountID={AccountID}", accountID);
            return StatusCode(500, new ErrorResponse { Message = "Error al guardar la configuración de alertas", Error = ex.Message });
        }
    }

    /// <summary>
    /// Guarda la pestaña "Límites de velocidad" (modo flota o por vehículo, límite global y overrides por placa)
    /// y sincroniza automáticamente attributes.speedLimit (en nudos) en Traccar para las unidades de esa cuenta.
    /// </summary>
    [HttpPut("limites-velocidad")]
    [ProducesResponseType(typeof(GuardarLimitesVelocidadResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SaveLimitesVelocidad(
        [FromQuery] string accountID,
        [FromBody] LimitesVelocidadDto body,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountID))
            return BadRequest(new ErrorResponse { Message = "El parámetro 'accountID' es requerido." });

        try
        {
            var saved = await _repository.SaveLimitesVelocidadAsync(accountID, body, ct);
            var syncResult = await _traccarSpeedLimit.SyncAccountSpeedLimitsAsync(accountID, saved, ct);

            return Ok(new GuardarLimitesVelocidadResponse
            {
                Modo = saved.Modo,
                LimiteFlotaKmh = saved.LimiteFlotaKmh,
                PorVehiculo = saved.PorVehiculo,
                TraccarSync = syncResult
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al guardar límites de velocidad para accountID={AccountID}", accountID);
            return StatusCode(500, new ErrorResponse { Message = "Error al guardar los límites de velocidad", Error = ex.Message });
        }
    }

    /// <summary>
    /// Guarda o actualiza los íconos asignados a una o varias unidades (pestaña "Íconos").
    /// </summary>
    [HttpPut("iconos")]
    [ProducesResponseType(typeof(Dictionary<string, string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SaveIconos(
        [FromQuery] string accountID,
        [FromBody] GuardarIconosRequest body,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountID))
            return BadRequest(new ErrorResponse { Message = "El parámetro 'accountID' es requerido." });

        try
        {
            var saved = await _repository.SaveIconosAsync(accountID, body, ct);
            return Ok(saved);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al guardar íconos para accountID={AccountID}", accountID);
            return StatusCode(500, new ErrorResponse { Message = "Error al guardar los íconos", Error = ex.Message });
        }
    }

    /// <summary>
    /// Restablece todas las unidades de la cuenta al ícono por defecto ("Auto").
    /// </summary>
    [HttpDelete("iconos")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetIconos(
        [FromQuery] string accountID,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountID))
            return BadRequest(new ErrorResponse { Message = "El parámetro 'accountID' es requerido." });

        try
        {
            await _repository.ResetIconosAsync(accountID, ct);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al restablecer íconos para accountID={AccountID}", accountID);
            return StatusCode(500, new ErrorResponse { Message = "Error al restablecer los íconos", Error = ex.Message });
        }
    }
}
