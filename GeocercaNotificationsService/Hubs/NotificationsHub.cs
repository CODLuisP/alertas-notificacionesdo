using Microsoft.AspNetCore.SignalR;

namespace GeocercaNotificationsService.Hubs;

/// <summary>
/// Hub de SignalR para notificaciones en tiempo real.
/// Los clientes se unen al grupo de su cuenta para recibir alertas.
/// </summary>
public sealed class NotificationsHub : Hub
{
    private readonly ILogger<NotificationsHub> _logger;

    public NotificationsHub(ILogger<NotificationsHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Agrega la conexión del cliente al grupo de la cuenta indicada.
    /// El cliente debe invocar este método inmediatamente después de conectarse.
    /// </summary>
    public async Task JoinAccountGroup(string accountID)
    {
        if (string.IsNullOrWhiteSpace(accountID))
        {
            _logger.LogWarning("JoinAccountGroup llamado con accountID vacío, connectionId={ConnectionId}",
                Context.ConnectionId);
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, accountID);

        _logger.LogInformation(
            "Conexión {ConnectionId} agregada al grupo de cuenta '{AccountID}'",
            Context.ConnectionId, accountID);
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("Cliente conectado: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Cliente desconectado: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
