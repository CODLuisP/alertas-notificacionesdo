using System.Text;
using Dapper;
using GeocercaNotificationsService.Models;
using MySqlConnector;

namespace GeocercaNotificationsService.Repositories;

public sealed class VisitasGeocercaRepository : IVisitasGeocercaRepository
{
    private readonly IConfiguration _configuration;

    public VisitasGeocercaRepository(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private MySqlConnection CreateConnection()
    {
        var connectionString = _configuration.GetConnectionString("GtsDatabase");
        return new MySqlConnection(connectionString);
    }

    public async Task<(IEnumerable<VisitaGeocerca> Visitas, int TotalRegistros)> GetVisitasAsync(
        string accountID,
        string? deviceID = null,
        int? geofenceID = null,
        DateTime? fechaDesde = null,
        DateTime? fechaHasta = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var where = new StringBuilder("WHERE accountID = @AccountID");
        var parameters = new DynamicParameters();
        parameters.Add("AccountID", accountID);

        if (!string.IsNullOrWhiteSpace(deviceID))
        {
            where.Append(" AND deviceID = @DeviceID");
            parameters.Add("DeviceID", deviceID);
        }

        if (geofenceID.HasValue)
        {
            where.Append(" AND geofenceID = @GeofenceID");
            parameters.Add("GeofenceID", geofenceID.Value);
        }

        if (fechaDesde.HasValue)
        {
            where.Append(" AND fecha_salida >= @FechaDesde");
            parameters.Add("FechaDesde", fechaDesde.Value);
        }

        if (fechaHasta.HasValue)
        {
            where.Append(" AND fecha_salida <= @FechaHasta");
            parameters.Add("FechaHasta", fechaHasta.Value);
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        // Contar total de registros
        var sqlCount = $"SELECT COUNT(*) FROM visitas_geocerca {where}";
        var totalRegistros = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(sqlCount, parameters, cancellationToken: ct));

        // Paginación
        var offset = (page - 1) * pageSize;
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", pageSize);

        var sqlData = $"""
            SELECT id, accountID, deviceID, geofenceID, geofenceName,
                   fecha_entrada AS FechaEntrada, fecha_salida AS FechaSalida,
                   duracion_minutos AS DuracionMinutos,
                   latitud_entrada AS LatitudEntrada, longitud_entrada AS LongitudEntrada,
                   latitud_salida AS LatitudSalida, longitud_salida AS LongitudSalida,
                   evento_entrada_id AS EventoEntradaId, evento_salida_id AS EventoSalidaId,
                   entrada_incompleta AS EntradaIncompleta,
                   salida_por_desvinculacion AS SalidaPorDesvinculacion,
                   fecha_registro AS FechaRegistro
            FROM visitas_geocerca {where}
            ORDER BY fecha_salida DESC
            LIMIT @PageSize OFFSET @Offset
            """;

        var visitas = await connection.QueryAsync<VisitaGeocerca>(
            new CommandDefinition(sqlData, parameters, cancellationToken: ct));

        return (visitas, totalRegistros);
    }

    public async Task<IEnumerable<ResumenVisitaGeocerca>> GetResumenAsync(
        string accountID,
        DateTime fechaDesde,
        DateTime fechaHasta,
        string? deviceID = null,
        int? geofenceID = null,
        CancellationToken ct = default)
    {
        var where = new StringBuilder("""
            WHERE accountID = @AccountID 
              AND fecha_salida >= @FechaDesde 
              AND fecha_salida <= @FechaHasta
            """);
        var parameters = new DynamicParameters();
        parameters.Add("AccountID", accountID);
        parameters.Add("FechaDesde", fechaDesde);
        parameters.Add("FechaHasta", fechaHasta);

        if (!string.IsNullOrWhiteSpace(deviceID))
        {
            where.Append(" AND deviceID = @DeviceID");
            parameters.Add("DeviceID", deviceID);
        }

        if (geofenceID.HasValue)
        {
            where.Append(" AND geofenceID = @GeofenceID");
            parameters.Add("GeofenceID", geofenceID.Value);
        }

        var sql = $"""
            SELECT 
                deviceID,
                geofenceID,
                geofenceName,
                COUNT(*) AS TotalVisitas,
                COALESCE(SUM(duracion_minutos), 0) AS MinutosTotales,
                COALESCE(ROUND(AVG(duracion_minutos)), 0) AS MinutosPromedioPorVisita
            FROM visitas_geocerca
            {where}
            GROUP BY deviceID, geofenceID, geofenceName
            ORDER BY deviceID, geofenceName
            """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var resultado = await connection.QueryAsync<ResumenVisitaGeocerca>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        return resultado;
    }

    public async Task<VisitaGeocerca?> GetVisitaPorIdAsync(
        int id,
        string accountID,
        CancellationToken ct = default)
    {
        const string sql = """
            SELECT id, accountID, deviceID, geofenceID, geofenceName,
                   fecha_entrada AS FechaEntrada, fecha_salida AS FechaSalida,
                   duracion_minutos AS DuracionMinutos,
                   latitud_entrada AS LatitudEntrada, longitud_entrada AS LongitudEntrada,
                   latitud_salida AS LatitudSalida, longitud_salida AS LongitudSalida,
                   evento_entrada_id AS EventoEntradaId, evento_salida_id AS EventoSalidaId,
                   entrada_incompleta AS EntradaIncompleta,
                   salida_por_desvinculacion AS SalidaPorDesvinculacion,
                   fecha_registro AS FechaRegistro
            FROM visitas_geocerca
            WHERE id = @Id AND accountID = @AccountID
            """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var visita = await connection.QueryFirstOrDefaultAsync<VisitaGeocerca>(
            new CommandDefinition(sql, new { Id = id, AccountID = accountID }, cancellationToken: ct));

        return visita;
    }

    public async Task<IEnumerable<AlertaGeocerca>> GetAlertasAsync(
        string accountID,
        string? deviceID = null,
        int? geofenceID = null,
        int limit = 50,
        CancellationToken ct = default)
    {
        // Las salidas por desvinculación no son salidas reales: no se listan como alertas.
        var where = new StringBuilder("""
            WHERE accountID = @AccountID AND eventType IN ('geofenceEnter', 'geofenceExit')
              AND NOT EXISTS (
                  SELECT 1 FROM visitas_geocerca v
                  WHERE v.evento_salida_id = deviceevent.id AND v.salida_por_desvinculacion = 1
              )
            """);
        var parameters = new DynamicParameters();
        parameters.Add("AccountID", accountID);

        if (!string.IsNullOrWhiteSpace(deviceID))
        {
            where.Append(" AND deviceID = @DeviceID");
            parameters.Add("DeviceID", deviceID);
        }

        if (geofenceID.HasValue)
        {
            where.Append(" AND geofenceID = @GeofenceID");
            parameters.Add("GeofenceID", geofenceID.Value);
        }

        if (limit < 1) limit = 50;
        if (limit > 200) limit = 200;
        parameters.Add("Limit", limit);

        var sql = $"""
            SELECT id, accountID, deviceID, eventType, serverTime,
                   latitude, longitude, speed, geofenceID, geofenceName, isEnviado
            FROM deviceevent
            {where}
            ORDER BY id DESC
            LIMIT @Limit
            """;

        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);

        var alertas = await connection.QueryAsync<AlertaGeocerca>(
            new CommandDefinition(sql, parameters, cancellationToken: ct));

        return alertas;
    }
}
