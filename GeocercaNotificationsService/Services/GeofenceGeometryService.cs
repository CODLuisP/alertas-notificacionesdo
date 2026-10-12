using System.Globalization;
using System.Text.RegularExpressions;
using Dapper;
using GeocercaNotificationsService.Models;
using MySqlConnector;

namespace GeocercaNotificationsService.Services;

public interface IGeofenceGeometryService
{
    /// <summary>
    /// true si un geofenceExit ocurrió con el vehículo todavía DENTRO de la geocerca.
    /// Eso pasa cuando se desvincula el vehículo de la geocerca (o se elimina la geocerca)
    /// mientras está adentro: el servidor de alertas lo reporta como salida aunque no se movió.
    /// </summary>
    Task<bool> IsUnlinkExitAsync(DeviceEvent exitEvent, CancellationToken ct);
}

/// <summary>
/// Evalúa la posición de un evento contra la forma guardada de la geocerca (geocercas.areaWkt,
/// mismo WKT que usa el servidor de alertas: "CIRCLE (lat lng, radio)" o "POLYGON ((lat lng, ...))").
/// </summary>
public sealed class GeofenceGeometryService : IGeofenceGeometryService
{
    // Solo se considera desvinculación si el punto está claramente adentro: cerca del borde
    // el ruido del GPS puede producir salidas reales con la posición apenas dentro.
    private const double BorderMarginMeters = 30;
    private const double EarthRadiusMeters = 6371008.8;

    private static readonly Regex CircleRegex = new(
        @"CIRCLE\s*\(\s*([-\d.]+)\s+([-\d.]+)\s*,\s*([-\d.]+)\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PointRegex = new(
        @"([-\d.]+)\s+([-\d.]+)",
        RegexOptions.Compiled);

    private readonly IConfiguration _configuration;
    private readonly ILogger<GeofenceGeometryService> _logger;

    public GeofenceGeometryService(IConfiguration configuration, ILogger<GeofenceGeometryService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<bool> IsUnlinkExitAsync(DeviceEvent exitEvent, CancellationToken ct)
    {
        if (exitEvent.GeofenceID is null) return false;
        if (exitEvent.Latitude == 0 && exitEvent.Longitude == 0) return false;

        try
        {
            var areaWkt = await GetAreaWktAsync(exitEvent, ct);
            if (string.IsNullOrWhiteSpace(areaWkt)) return false;

            var inside = IsClearlyInside(exitEvent.Latitude, exitEvent.Longitude, areaWkt);
            if (inside)
            {
                _logger.LogInformation(
                    "Salida por desvinculación detectada: evento id={EventId}, deviceID={DeviceID}, " +
                    "geofenceID={GeofenceID}. La posición ({Lat}, {Lon}) sigue dentro de la geocerca.",
                    exitEvent.Id, exitEvent.DeviceID, exitEvent.GeofenceID,
                    exitEvent.Latitude, exitEvent.Longitude);
            }
            return inside;
        }
        catch (Exception ex)
        {
            // Ante cualquier duda se trata como salida normal (comportamiento anterior).
            _logger.LogWarning(ex,
                "No se pudo evaluar la forma de la geocerca para el evento id={EventId}. Se trata como salida normal.",
                exitEvent.Id);
            return false;
        }
    }

    private async Task<string?> GetAreaWktAsync(DeviceEvent evt, CancellationToken ct)
    {
        const string sql = """
            SELECT * FROM geocercas
            WHERE geofenceID = @GeofenceID AND accountID = @AccountID
            LIMIT 1
            """;

        var connectionString = _configuration.GetConnectionString("GtsDatabase");
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(ct);

        var row = await connection.QueryFirstOrDefaultAsync(
            new CommandDefinition(sql, new { evt.GeofenceID, evt.AccountID }, cancellationToken: ct));
        if (row is not IDictionary<string, object> columns) return null;

        // La columna puede llamarse areaWkt o area_wkt según cómo se creó la tabla
        foreach (var (name, value) in columns)
        {
            if (name.Replace("_", "").Equals("areawkt", StringComparison.OrdinalIgnoreCase))
            {
                return value as string;
            }
        }
        return null;
    }

    public static bool IsClearlyInside(double lat, double lon, string areaWkt)
    {
        var circle = CircleRegex.Match(areaWkt);
        if (circle.Success)
        {
            var centerLat = Parse(circle.Groups[1].Value);
            var centerLon = Parse(circle.Groups[2].Value);
            var radius = Parse(circle.Groups[3].Value);
            return DistanceMeters(lat, lon, centerLat, centerLon) <= radius - BorderMarginMeters;
        }

        if (areaWkt.TrimStart().StartsWith("POLYGON", StringComparison.OrdinalIgnoreCase))
        {
            var ring = PointRegex.Matches(areaWkt)
                .Select(m => (Lat: Parse(m.Groups[1].Value), Lon: Parse(m.Groups[2].Value)))
                .ToList();
            if (ring.Count < 3) return false;
            return PointInPolygon(lat, lon, ring) && DistanceToRingMeters(lat, lon, ring) >= BorderMarginMeters;
        }

        return false;
    }

    private static double Parse(string value) => double.Parse(value, CultureInfo.InvariantCulture);

    private static double DistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        static double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var h = Math.Pow(Math.Sin(dLat / 2), 2) +
                Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Sqrt(h));
    }

    // Ray casting sobre lat/lon: exacto de sobra a escala de ciudad.
    private static bool PointInPolygon(double lat, double lon, IReadOnlyList<(double Lat, double Lon)> ring)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var (yi, xi) = ring[i];
            var (yj, xj) = ring[j];
            var intersects = (yi > lat) != (yj > lat) &&
                             lon < (xj - xi) * (lat - yi) / (yj - yi) + xi;
            if (intersects) inside = !inside;
        }
        return inside;
    }

    // Distancia mínima del punto a los lados del polígono, en una proyección plana local (metros).
    private static double DistanceToRingMeters(double lat, double lon, IReadOnlyList<(double Lat, double Lon)> ring)
    {
        var metersPerDegLat = Math.PI * EarthRadiusMeters / 180;
        var metersPerDegLon = metersPerDegLat * Math.Cos(lat * Math.PI / 180);
        (double X, double Y) ToXY((double Lat, double Lon) p) =>
            ((p.Lon - lon) * metersPerDegLon, (p.Lat - lat) * metersPerDegLat);

        var min = double.MaxValue;
        for (int i = 0; i < ring.Count; i++)
        {
            var a = ToXY(ring[i]);
            var b = ToXY(ring[(i + 1) % ring.Count]);
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var lengthSq = dx * dx + dy * dy;
            // El punto evaluado es el origen (0, 0) de la proyección
            var t = lengthSq == 0 ? 0 : Math.Clamp(-(a.X * dx + a.Y * dy) / lengthSq, 0, 1);
            var px = a.X + t * dx;
            var py = a.Y + t * dy;
            min = Math.Min(min, Math.Sqrt(px * px + py * py));
        }
        return min;
    }
}
