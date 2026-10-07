using GeocercaNotificationsService.Models;

namespace GeocercaNotificationsService.Repositories;

/// <summary>
/// Interfaz para consultas sobre la tabla gts.visitas_geocerca.
/// </summary>
public interface IVisitasGeocercaRepository
{
    Task<(IEnumerable<VisitaGeocerca> Visitas, int TotalRegistros)> GetVisitasAsync(
        string accountID,
        string? deviceID = null,
        int? geofenceID = null,
        DateTime? fechaDesde = null,
        DateTime? fechaHasta = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default);

    Task<IEnumerable<ResumenVisitaGeocerca>> GetResumenAsync(
        string accountID,
        DateTime fechaDesde,
        DateTime fechaHasta,
        string? deviceID = null,
        int? geofenceID = null,
        CancellationToken ct = default);

    Task<VisitaGeocerca?> GetVisitaPorIdAsync(
        int id,
        string accountID,
        CancellationToken ct = default);

    Task<IEnumerable<AlertaGeocerca>> GetAlertasAsync(
        string accountID,
        string? deviceID = null,
        int? geofenceID = null,
        int limit = 50,
        CancellationToken ct = default);
}
