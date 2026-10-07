using GeocercaNotificationsService.Models;

namespace GeocercaNotificationsService.Services;

/// <summary>
/// Cliente HTTP aislado para interactuar con la API externa de WhatsApp.
/// </summary>
public interface IWhatsAppClient
{
    /// <summary>
    /// Envía un mensaje de texto por WhatsApp a un número específico.
    /// </summary>
    /// <param name="phone">Número de teléfono de destino (con código de país, cualquier formato).</param>
    /// <param name="message">Texto plano del mensaje.</param>
    /// <param name="ct">Token de cancelación.</param>
    /// <returns>Resultado del envío indicando éxito/fallo y código HTTP.</returns>
    Task<WhatsAppSendResult> SendTextMessageAsync(string phone, string message, CancellationToken ct = default);

    /// <summary>
    /// Normaliza un número telefónico eliminando signos '+', espacios, guiones y caracteres no numéricos.
    /// </summary>
    string NormalizePhone(string? phone);
}
