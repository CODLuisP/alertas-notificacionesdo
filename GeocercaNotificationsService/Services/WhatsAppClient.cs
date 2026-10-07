using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GeocercaNotificationsService.Models;

namespace GeocercaNotificationsService.Services;

/// <summary>
/// Implementación del cliente HTTP para la API de WhatsApp de Velsat.
/// </summary>
public sealed class WhatsAppClient : IWhatsAppClient
{
    private readonly HttpClient _httpClient;
    private readonly WhatsAppSettings _settings;
    private readonly ILogger<WhatsAppClient> _logger;

    public WhatsAppClient(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<WhatsAppClient> logger)
    {
        _httpClient = httpClient;
        _settings = WhatsAppSettings.Load(configuration);
        _logger = logger;
    }

    /// <inheritdoc />
    public string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return string.Empty;

        // Quitar espacios, signos +, guiones, paréntesis y cualquier caracter no numérico
        var digitsOnly = Regex.Replace(phone, @"[^\d]", string.Empty);

        // Si es número peruano de 9 dígitos empezando en 9 sin código de país, anteponer 51
        if (digitsOnly.Length == 9 && digitsOnly.StartsWith('9'))
        {
            digitsOnly = "51" + digitsOnly;
        }

        return digitsOnly;
    }

    /// <inheritdoc />
    public async Task<WhatsAppSendResult> SendTextMessageAsync(string phone, string message, CancellationToken ct = default)
    {
        var normalizedPhone = NormalizePhone(phone);
        if (string.IsNullOrWhiteSpace(normalizedPhone))
        {
            _logger.LogWarning("Intento de envío de WhatsApp con teléfono inválido o vacío: '{RawPhone}'", phone);
            return WhatsAppSendResult.Failure(phone, 400, "Número de teléfono inválido o vacío");
        }

        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _logger.LogError("No se ha configurado la variable de entorno WHATSAPP_API_KEY o WhatsApp:ApiKey");
            return WhatsAppSendResult.Failure(normalizedPhone, 500, "WHATSAPP_API_KEY no configurada");
        }

        var payload = new WhatsAppSendRequest
        {
            Phone = normalizedPhone,
            Type = "texto",
            Text = message
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, _settings.ApiUrl)
        {
            Content = content
        };

        // Agregar header de autenticación x-api-key requerido por el servicio
        request.Headers.Add("x-api-key", _settings.ApiKey);

        // Timeout configurable
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(_settings.TimeoutMs));

        var maskedPhone = MaskPhone(normalizedPhone);
        _logger.LogInformation("Enviando mensaje WhatsApp a {Phone}...", maskedPhone);

        try
        {
            using var response = await _httpClient.SendAsync(request, cts.Token);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "WhatsApp enviado exitosamente a {Phone}. HTTP {StatusCode}",
                    maskedPhone, (int)response.StatusCode);
                return WhatsAppSendResult.Success(normalizedPhone, (int)response.StatusCode, responseBody);
            }

            _logger.LogWarning(
                "Fallo al enviar WhatsApp a {Phone}. HTTP {StatusCode}. Respuesta: {Response}",
                maskedPhone, (int)response.StatusCode, responseBody);

            return WhatsAppSendResult.Failure(
                normalizedPhone,
                (int)response.StatusCode,
                $"HTTP {(int)response.StatusCode}: {responseBody}",
                responseBody);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _logger.LogError("Timeout ({Timeout} ms) superado al enviar WhatsApp a {Phone}", _settings.TimeoutMs, maskedPhone);
            return WhatsAppSendResult.Failure(normalizedPhone, 408, $"Timeout de {_settings.TimeoutMs} ms superado al conectar con WhatsApp");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error de red al enviar WhatsApp a {Phone}", maskedPhone);
            return WhatsAppSendResult.Failure(normalizedPhone, 500, ex.Message);
        }
    }

    private static string MaskPhone(string phone)
    {
        if (phone.Length <= 4) return "****";
        return string.Concat("".PadLeft(phone.Length - 4, '*'), phone.AsSpan(phone.Length - 4));
    }
}
