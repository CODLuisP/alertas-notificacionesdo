namespace GeocercaNotificationsService.Models;

/// <summary>
/// Configuración de conexión a la API REST de Traccar para sincronización de límites de velocidad.
/// Se carga desde variables de entorno Traccar__BaseUrl, Traccar__Username, Traccar__Password.
/// </summary>
public sealed class TraccarSettings
{
    public string BaseUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxConcurrency { get; set; } = 5;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(BaseUrl) &&
        !string.IsNullOrWhiteSpace(Username) &&
        !string.IsNullOrWhiteSpace(Password);

    public static TraccarSettings Load(IConfiguration configuration)
    {
        return new TraccarSettings
        {
            BaseUrl = (configuration["Traccar:BaseUrl"] ?? configuration["TRACCAR_BASE_URL"] ?? string.Empty).Trim().TrimEnd('/'),
            Username = (configuration["Traccar:Username"] ?? configuration["TRACCAR_USERNAME"] ?? string.Empty).Trim(),
            Password = configuration["Traccar:Password"] ?? configuration["TRACCAR_PASSWORD"] ?? string.Empty,
            TimeoutSeconds = configuration.GetValue("Traccar:TimeoutSeconds", 30),
            MaxConcurrency = Math.Clamp(configuration.GetValue("Traccar:MaxConcurrency", 5), 1, 15)
        };
    }
}

/// <summary>
/// Resultado de la sincronización de attributes.speedLimit en Traccar.
/// </summary>
public sealed class TraccarSpeedLimitSyncResult
{
    public bool Habilitado { get; set; }
    public int TotalUnidadesCuenta { get; set; }
    public int Actualizadas { get; set; }
    public int SinCambios { get; set; }
    public int Errores { get; set; }
    public string? Mensaje { get; set; }
}
