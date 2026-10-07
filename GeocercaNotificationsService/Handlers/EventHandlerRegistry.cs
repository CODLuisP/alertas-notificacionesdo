namespace GeocercaNotificationsService.Handlers;

/// <summary>
/// Resuelve qué IEventHandler debe procesar un tipo de evento dado.
/// Al agregar nuevos handlers, solo hay que registrarlos en el DI container
/// y este registry los descubrirá automáticamente.
/// </summary>
public sealed class EventHandlerRegistry
{
    private readonly Dictionary<string, IEventHandler> _handlers;

    public EventHandlerRegistry(IEnumerable<IEventHandler> handlers)
    {
        _handlers = new Dictionary<string, IEventHandler>(StringComparer.OrdinalIgnoreCase);

        foreach (var handler in handlers)
        {
            foreach (var eventType in handler.SupportedEventTypes)
            {
                _handlers[eventType] = handler;
            }
        }
    }

    /// <summary>
    /// Retorna el handler correspondiente al tipo de evento, o null si no existe.
    /// </summary>
    public IEventHandler? GetHandler(string eventType)
    {
        _handlers.TryGetValue(eventType, out var handler);
        return handler;
    }

    /// <summary>
    /// Retorna todos los tipos de evento soportados (para armar la cláusula IN del SELECT).
    /// </summary>
    public IReadOnlyCollection<string> SupportedEventTypes => _handlers.Keys;
}
