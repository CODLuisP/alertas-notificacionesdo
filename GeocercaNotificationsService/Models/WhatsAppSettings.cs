namespace GeocercaNotificationsService.Models;

/// <summary>
/// Opciones de configuración para el servicio de mensajería WhatsApp.
/// Lee variables de entorno prioritariamente (WHATSAPP_ENABLED, WHATSAPP_API_URL, WHATSAPP_API_KEY, WHATSAPP_TIMEOUT_MS)
/// con fallback a la sección "WhatsApp" en appsettings.json.
/// </summary>
public sealed class WhatsAppSettings
{
    public const string SectionName = "WhatsApp";

    /// <summary>
    /// Activa o desactiva el envío automático de notificaciones por WhatsApp.
    /// Por defecto: false (requiere activación explícita tras ejecutar migraciones SQL).
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// URL del endpoint de envío de WhatsApp.
    /// Por defecto: https://do.velsat.pe:8443/whatsapp/api/send/single
    /// </summary>
    public string ApiUrl { get; set; } = "https://do.velsat.pe:8443/whatsapp/api/send/single";

    /// <summary>
    /// Clave de API para autenticación con el servicio de WhatsApp.
    /// Nunca debe ser hardcodeada ni expuesta en logs.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Tiempo de espera máximo en milisegundos para cada solicitud HTTP a WhatsApp.
    /// Por defecto: 10000 (10 segundos).
    /// </summary>
    public int TimeoutMs { get; set; } = 10000;

    /// <summary>
    /// Pausa entre envíos a diferentes destinatarios para no saturar el canal (en ms).
    /// Por defecto: 400 ms.
    /// </summary>
    public int DelayBetweenSendsMs { get; set; } = 400;

    /// <summary>
    /// Carga las configuraciones combinando IConfiguration y variables de entorno del sistema.
    /// </summary>
    public static WhatsAppSettings Load(IConfiguration configuration)
    {
        var settings = new WhatsAppSettings();

        // 1. Leer desde appsettings.json si existe la sección
        var section = configuration.GetSection(SectionName);
        if (section.Exists())
        {
            settings.Enabled = section.GetValue("Enabled", false);
            settings.ApiUrl = section.GetValue("ApiUrl", settings.ApiUrl) ?? settings.ApiUrl;
            settings.ApiKey = section.GetValue("ApiKey", string.Empty) ?? string.Empty;
            settings.TimeoutMs = section.GetValue("TimeoutMs", settings.TimeoutMs);
            settings.DelayBetweenSendsMs = section.GetValue("DelayBetweenSendsMs", settings.DelayBetweenSendsMs);
        }

        // 2. Sobrescribir con variables de entorno del sistema (máxima prioridad)
        var envEnabled = Environment.GetEnvironmentVariable("WHATSAPP_ENABLED");
        if (!string.IsNullOrWhiteSpace(envEnabled))
        {
            settings.Enabled = bool.TryParse(envEnabled, out var b) ? b : envEnabled == "1";
        }

        var envUrl = Environment.GetEnvironmentVariable("WHATSAPP_API_URL");
        if (!string.IsNullOrWhiteSpace(envUrl))
        {
            settings.ApiUrl = envUrl;
        }

        var envKey = Environment.GetEnvironmentVariable("WHATSAPP_API_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            settings.ApiKey = envKey;
        }

        var envTimeout = Environment.GetEnvironmentVariable("WHATSAPP_TIMEOUT_MS");
        if (!string.IsNullOrWhiteSpace(envTimeout) && int.TryParse(envTimeout, out var timeout))
        {
            settings.TimeoutMs = timeout;
        }

        return settings;
    }
}
