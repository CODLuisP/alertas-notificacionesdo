using System.Globalization;
using GeocercaNotificationsService.Models;
using GeocercaNotificationsService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GeocercaNotificationsService.Controllers;

/// <summary>
/// Controlador para pruebas de conectividad y envío manual de mensajes a través del canal de WhatsApp.
/// </summary>
[Route("api/whatsapp")]
[ApiController]
[Authorize]
[Produces("application/json")]
public sealed class WhatsAppTestController : ControllerBase
{
    private readonly IWhatsAppClient _whatsAppClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WhatsAppTestController> _logger;

    public WhatsAppTestController(
        IWhatsAppClient whatsAppClient,
        IConfiguration configuration,
        ILogger<WhatsAppTestController> logger)
    {
        _whatsAppClient = whatsAppClient;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Consulta el estado actual de la configuración del servicio de WhatsApp (valores de entorno y flags).
    /// </summary>
    /// <response code="200">Retorna los parámetros de configuración actuales (con la API key protegida).</response>
    [HttpGet("status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetStatus()
    {
        var settings = WhatsAppSettings.Load(_configuration);
        var hasApiKey = !string.IsNullOrWhiteSpace(settings.ApiKey);

        return Ok(new
        {
            enabled = settings.Enabled,
            apiUrl = settings.ApiUrl,
            apiKeyConfigured = hasApiKey,
            timeoutMs = settings.TimeoutMs,
            delayBetweenSendsMs = settings.DelayBetweenSendsMs,
            serverUtcTime = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Envía un mensaje de prueba de WhatsApp al número especificado para verificar conectividad y credenciales.
    /// </summary>
    /// <param name="request">Datos del destinatario y mensaje opcional.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <remarks>
    /// Si el campo 'message' se omite o está vacío, se envía automáticamente una alerta de ejemplo de ENTRADA a geocerca.
    /// El número telefónico se normaliza automáticamente quitando signos '+', espacios y guiones.
    /// </remarks>
    /// <response code="200">Retorna el resultado directo de la llamada a la API de WhatsApp.</response>
    /// <response code="400">Si el número telefónico es inválido.</response>
    /// <response code="401">Si el token estático de autorización no fue proporcionado o es inválido.</response>
    [HttpPost("test-send")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> TestSend([FromBody] TestWhatsAppRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Phone))
        {
            return BadRequest(new ErrorResponse
            {
                Error = "El campo 'phone' es obligatorio. Ejemplo: '51987654321' o '+51 987 654 321'."
            });
        }

        var normalizedPhone = _whatsAppClient.NormalizePhone(request.Phone);
        if (normalizedPhone.Length < 8)
        {
            return BadRequest(new ErrorResponse
            {
                Error = $"El número '{request.Phone}' no tiene un formato válido tras normalizar: '{normalizedPhone}'."
            });
        }

        // Construir mensaje por defecto si no fue suministrado
        var messageToSend = request.Message;
        if (string.IsNullOrWhiteSpace(messageToSend))
        {
            var nowLima = TimeZoneInfo.ConvertTime(
                DateTimeOffset.UtcNow,
                ResolveLimaTimeZone());

            messageToSend = $"""
                🟢 *ENTRADA a geocerca (Prueba de Integración)*
                Vehículo: test-geo-001
                Geocerca: Prueba
                Hora: {nowLima:dd/MM/yyyy HH:mm}
                📍 https://maps.google.com/?q=-12.017271,-77.070781
                """;
        }

        _logger.LogInformation("Ejecutando prueba de envío WhatsApp a {Phone}...", normalizedPhone);

        var result = await _whatsAppClient.SendTextMessageAsync(normalizedPhone, messageToSend, ct);

        return Ok(new
        {
            success = result.IsSuccess,
            httpStatusCode = result.StatusCode,
            normalizedPhone = result.NormalizedPhone,
            messageSent = messageToSend,
            apiResponse = result.ResponseBody,
            errorMessage = result.ErrorMessage
        });
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
}
