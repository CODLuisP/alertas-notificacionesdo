using GeocercaNotificationsService.Models;
using GeocercaNotificationsService.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeocercaNotificationsService.Controllers;

/// <summary>
/// Controlador para reportes e historial de visitas a geocercas en tiempo real.
/// Requiere autorización mediante el token estático permanente.
/// </summary>
[Route("api/reportes/geocercas")]
[ApiController]
[Authorize]
[Produces("application/json")]
public sealed class ReportesGeocercaController : ControllerBase
{
    private readonly IVisitasGeocercaRepository _repository;
    private readonly ILogger<ReportesGeocercaController> _logger;

    public ReportesGeocercaController(
        IVisitasGeocercaRepository repository,
        ILogger<ReportesGeocercaController> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Lista el historial de visitas de geocerca paginado y ordenado cronológicamente para una cuenta.
    /// </summary>
    /// <remarks>
    /// Devuelve las visitas registradas para el accountID especificado, ordenadas por fecha_salida descendente (más reciente primero).
    /// Cada visita representa una estadía resuelta con su duración calculada.
    /// Si no se encuentran resultados con los filtros aplicados, responde 200 con una lista vacía.
    /// </remarks>
    /// <param name="accountID">Identificador de la cuenta/usuario a consultar (OBLIGATORIO). Ejemplo: 'movilbus'.</param>
    /// <param name="deviceID">Placa o identificador del vehículo a filtrar (opcional). Ejemplo: 'ABC-123'.</param>
    /// <param name="geofenceID">Identificador numérico de la geocerca a filtrar (opcional). Ejemplo: 15.</param>
    /// <param name="fechaDesde">Fecha de inicio del rango de búsqueda sobre fecha_salida en formato ISO 8601 (opcional). Ejemplo: '2026-09-01T00:00:00Z'.</param>
    /// <param name="fechaHasta">Fecha de fin del rango de búsqueda sobre fecha_salida en formato ISO 8601 (opcional). Ejemplo: '2026-09-23T23:59:59Z'.</param>
    /// <param name="page">Número de página solicitada (opcional, valor por defecto: 1, mínimo: 1).</param>
    /// <param name="pageSize">Cantidad de registros por página (opcional, valor por defecto: 20, máximo permitido: 100).</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <response code="200">Retorna la colección de visitas de la página solicitada junto con la metadata de paginación.</response>
    /// <response code="400">Si accountID falta o si fechaDesde es posterior a fechaHasta.</response>
    /// <response code="401">Si el token estático de autorización no fue enviado o no es válido.</response>
    /// <response code="500">Si ocurre un error interno en el servidor o base de datos.</response>
    [HttpGet("visitas")]
    [ProducesResponseType(typeof(VisitasPaginadas), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetVisitas(
        [FromQuery] string accountID,
        [FromQuery] string? deviceID = null,
        [FromQuery] int? geofenceID = null,
        [FromQuery] DateTime? fechaDesde = null,
        [FromQuery] DateTime? fechaHasta = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(accountID))
                return BadRequest(new ErrorResponse { Message = "El parámetro 'accountID' es requerido." });

            // Validaciones
            if (fechaDesde.HasValue && fechaHasta.HasValue && fechaDesde.Value > fechaHasta.Value)
                return BadRequest(new ErrorResponse { Message = "fechaDesde no puede ser posterior a fechaHasta." });

            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            var (visitas, totalRegistros) = await _repository.GetVisitasAsync(
                accountID.Trim(), deviceID, geofenceID, fechaDesde, fechaHasta, page, pageSize, ct);

            var totalPaginas = (int)Math.Ceiling((double)totalRegistros / pageSize);

            var resultado = new VisitasPaginadas
            {
                Visitas = visitas,
                TotalRegistros = totalRegistros,
                PaginaActual = page,
                TotalPaginas = totalPaginas,
                PageSize = pageSize
            };

            return Ok(resultado);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener las visitas de geocerca para accountID={AccountID}", accountID);
            return StatusCode(500, new ErrorResponse { Message = "Error al obtener las visitas de geocerca", Error = ex.Message });
        }
    }

    /// <summary>
    /// Reporte consolidado de visitas y tiempo acumulado por vehículo y geocerca para una cuenta.
    /// </summary>
    /// <remarks>
    /// Agrupa las visitas en el rango de fechas obligatorio por cada combinación de deviceID y geofenceID para el accountID especificado.
    /// Calcula el total de visitas, los minutos acumulados y el promedio de permanencia por visita.
    /// Si no se especifica deviceID o geofenceID, se incluyen todos los vehículos y geocercas de esa cuenta con actividad en el rango.
    /// </remarks>
    /// <param name="accountID">Identificador de la cuenta/usuario a consultar (OBLIGATORIO). Ejemplo: 'movilbus'.</param>
    /// <param name="fechaDesde">Fecha de inicio del rango sobre fecha_salida en formato ISO 8601 (OBLIGATORIO). Ejemplo: '2026-09-01T00:00:00Z'.</param>
    /// <param name="fechaHasta">Fecha de fin del rango sobre fecha_salida en formato ISO 8601 (OBLIGATORIO). Ejemplo: '2026-09-23T23:59:59Z'.</param>
    /// <param name="deviceID">Placa de un vehículo específico a consultar (opcional, por defecto todos los vehículos).</param>
    /// <param name="geofenceID">ID de una geocerca específica a consultar (opcional, por defecto todas las geocercas).</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <response code="200">Retorna la lista de métricas agregadas por cada par vehículo-geocerca.</response>
    /// <response code="400">Si falta accountID, faltan fechaDesde/fechaHasta o si fechaDesde es posterior a fechaHasta.</response>
    /// <response code="401">Si el token estático de autorización no fue enviado o no es válido.</response>
    /// <response code="500">Si ocurre un error interno en el servidor o base de datos.</response>
    [HttpGet("resumen")]
    [ProducesResponseType(typeof(IEnumerable<ResumenVisitaGeocerca>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetResumen(
        [FromQuery] string accountID,
        [FromQuery] DateTime? fechaDesde = null,
        [FromQuery] DateTime? fechaHasta = null,
        [FromQuery] string? deviceID = null,
        [FromQuery] int? geofenceID = null,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(accountID))
                return BadRequest(new ErrorResponse { Message = "El parámetro 'accountID' es requerido." });

            if (!fechaDesde.HasValue || !fechaHasta.HasValue)
                return BadRequest(new ErrorResponse { Message = "fechaDesde y fechaHasta son requeridos." });

            if (fechaDesde.Value > fechaHasta.Value)
                return BadRequest(new ErrorResponse { Message = "fechaDesde no puede ser posterior a fechaHasta." });

            var resumen = await _repository.GetResumenAsync(
                accountID.Trim(), fechaDesde.Value, fechaHasta.Value, deviceID, geofenceID, ct);

            return Ok(resumen);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener el resumen de geocercas para accountID={AccountID}", accountID);
            return StatusCode(500, new ErrorResponse { Message = "Error al obtener el resumen de geocercas", Error = ex.Message });
        }
    }

    /// <summary>
    /// Obtiene el detalle completo de una visita específica por su ID validando la cuenta.
    /// </summary>
    /// <remarks>
    /// Valida que la visita pertenezca al accountID especificado.
    /// Si la visita no existe o pertenece a otra cuenta distinta, responde 404.
    /// </remarks>
    /// <param name="id">Identificador único de la visita en visitas_geocerca. Ejemplo: 1.</param>
    /// <param name="accountID">Identificador de la cuenta/usuario propietaria (OBLIGATORIO). Ejemplo: 'movilbus'.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <response code="200">Retorna los datos completos de la visita de geocerca.</response>
    /// <response code="400">Si el parámetro accountID no fue enviado.</response>
    /// <response code="401">Si el token estático de autorización no fue enviado o no es válido.</response>
    /// <response code="404">Si la visita no existe o pertenece a otra cuenta.</response>
    /// <response code="500">Si ocurre un error interno en el servidor o base de datos.</response>
    [HttpGet("visitas/{id:int}")]
    [ProducesResponseType(typeof(VisitaGeocerca), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetVisitaPorId(
        int id,
        [FromQuery] string accountID,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(accountID))
                return BadRequest(new ErrorResponse { Message = "El parámetro 'accountID' es requerido." });

            var visita = await _repository.GetVisitaPorIdAsync(id, accountID.Trim(), ct);

            if (visita is null)
                return NotFound(new ErrorResponse { Message = $"No se encontró la visita con id {id} para la cuenta '{accountID}'." });

            return Ok(visita);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener el detalle de la visita con id={Id}", id);
            return StatusCode(500, new ErrorResponse { Message = "Error al obtener el detalle de la visita", Error = ex.Message });
        }
    }

    /// <summary>
    /// Lista el historial de alertas individuales (entradas y salidas) registradas en deviceevent.
    /// </summary>
    /// <remarks>
    /// Devuelve las alertas individuales ocurridas para una cuenta (cada entrada y cada salida por separado),
    /// ordenadas de la más reciente a la más antigua. Útil para que las alertas no desaparezcan al recargar la página.
    /// </remarks>
    /// <param name="accountID">Identificador de la cuenta/usuario a consultar (OBLIGATORIO). Ejemplo: 'movilbus'.</param>
    /// <param name="deviceID">Placa o identificador del vehículo a filtrar (opcional). Ejemplo: 's110-cml340'.</param>
    /// <param name="geofenceID">Identificador numérico de la geocerca a filtrar (opcional). Ejemplo: 5.</param>
    /// <param name="limit">Cantidad máxima de alertas a devolver (opcional, valor por defecto: 50, máx: 200).</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <response code="200">Retorna la colección de alertas individuales.</response>
    /// <response code="400">Si accountID falta.</response>
    /// <response code="401">Si el token estático de autorización no fue enviado o no es válido.</response>
    /// <response code="500">Si ocurre un error interno en el servidor o base de datos.</response>
    [HttpGet("alertas")]
    [ProducesResponseType(typeof(IEnumerable<AlertaGeocerca>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetAlertas(
        [FromQuery] string accountID,
        [FromQuery] string? deviceID = null,
        [FromQuery] int? geofenceID = null,
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(accountID))
                return BadRequest(new ErrorResponse { Message = "El parámetro 'accountID' es requerido." });

            var alertas = await _repository.GetAlertasAsync(
                accountID.Trim(), deviceID, geofenceID, limit, ct);

            return Ok(alertas);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al obtener las alertas de geocerca para accountID={AccountID}", accountID);
            return StatusCode(500, new ErrorResponse { Message = "Error al obtener las alertas de geocerca", Error = ex.Message });
        }
    }
}
