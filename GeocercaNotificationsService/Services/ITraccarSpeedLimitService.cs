using GeocercaNotificationsService.Models;

namespace GeocercaNotificationsService.Services;

public interface ITraccarSpeedLimitService
{
    /// <summary>
    /// Sincroniza attributes.speedLimit (en nudos: kmh / 1.852) en Traccar
    /// exclusivamente para las unidades cuyo attributes.accountID coincide con la cuenta indicada.
    /// </summary>
    Task<TraccarSpeedLimitSyncResult> SyncAccountSpeedLimitsAsync(
        string accountID,
        LimitesVelocidadDto limites,
        CancellationToken ct = default);
}
