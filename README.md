# GeocercaNotificationsService

Microservicio .NET 8 con SignalR para notificaciones en tiempo real de eventos de geocerca. Lee directamente de la base de datos `gts.deviceevent` — no recibe webhooks ni depende de otro backend.

## Arquitectura

```
┌──────────────┐     polling cada N seg      ┌──────────────────┐
│  MySQL (gts) │  ◄─── SELECT isEnviado=0 ── │  Polling Service │
│  deviceevent │  ─── UPDATE isEnviado=1 ──► │  (Background)    │
│              │  ─── INSERT visitas ──────► │                  │
└──────┬───────┘                             └────────┬─────────┘
       │                                              │
       ▼ (visitas_geocerca)                           ▼
┌──────────────┐                             ┌──────────────────┐
│  Reportes    │ ◄── JWT multi-cuenta        │  SignalR Hub      │
│  Swagger API │     (/api/reportes/...)     │  /notificationsHub│
└──────────────┘                             └────────┬─────────┘
                                                      │
                                                      ▼
                                             ┌──────────────────┐
                                             │  Frontend(s)      │
                                             │  (por accountID)  │
                                             └──────────────────┘
```

## Token Estático Permanente (Master Token)

El microservicio utiliza un **Token Estático Permanente** configurado en `appsettings.json` (`StaticApiToken`):

```text
trackvel-static-token-2026
```

- **Nunca expira**: Puedes copiarlo y pegarlo una sola vez en el `appsettings.json`, variables de entorno o `Program.cs` de tus otros proyectos o frontend.
- **Forma de envío en peticiones HTTP**:
  - Header estándar: `Authorization: Bearer trackvel-static-token-2026`
  - O header alternativo: `X-API-KEY: trackvel-static-token-2026`

## Swagger UI & Documentación

- 👉 **`http://localhost:5000/swagger`** (o directamente **`http://localhost:5000`**)

Para autorizarte en Swagger:
1. Haz clic en el botón verde **Authorize** (arriba a la derecha).
2. Pega `trackvel-static-token-2026` en el campo **Value**.
3. Haz clic en **Authorize** y luego en **Close**.

## Endpoints de Reportes de Geocerca (REST API)

Todas las rutas requieren el token estático en el header y reciben el parámetro obligatorio **`accountID`**:

| Método | Ruta | Parámetro Obligatorio | Descripción |
|---|---|---|---|
| `GET` | `/api/reportes/geocercas/visitas` | `accountID` | Historial de visitas paginado con filtros (`deviceID`, `geofenceID`, `fechaDesde`, `fechaHasta`, `page`, `pageSize`) |
| `GET` | `/api/reportes/geocercas/resumen` | `accountID`, `fechaDesde`, `fechaHasta` | Métricas de visitas y minutos acumulados por vehículo y geocerca |
| `GET` | `/api/reportes/geocercas/visitas/{id}` | `accountID` | Detalle completo de una visita por ID |

## Configuración

### Variables de configuración

| Variable | Descripción | Default |
|---|---|---|
| `ConnectionStrings__GtsDatabase` | Cadena de conexión MySQL a la base `gts` | *(placeholder)* |
| `PollingIntervalSeconds` | Intervalo de sondeo en segundos | `2` |
| `AllowedOrigin` | Origen permitido para CORS (URL del frontend) | `http://localhost:3000` |
| `ASPNETCORE_URLS` | Puerto(s) de escucha | `http://+:5050` |

Estas variables se pueden configurar de tres formas:
1. En `appsettings.json` (para desarrollo local)
2. Como variables de entorno (para Docker o producción) — usa `__` como separador de niveles (ej: `ConnectionStrings__GtsDatabase`)
3. Argumentos de línea de comandos

### Ejemplo: appsettings.json

```json
{
  "ConnectionStrings": {
    "GtsDatabase": "Server=192.168.1.100;Port=3306;Database=gts;User=mi_usuario;Password=mi_password;"
  },
  "PollingIntervalSeconds": 2,
  "AllowedOrigin": "http://mi-frontend.com"
}
```

## Ejecución

### Desarrollo local

```bash
cd GeocercaNotificationsService
dotnet restore
dotnet run
```

### Docker

**Con docker-compose (recomendado):**

```bash
# Edita docker-compose.yml con tus valores y luego:
docker-compose up -d --build
```

**Con docker run:**

```bash
docker build -t geocerca-notifications ./GeocercaNotificationsService

docker run -d \
  --name geocerca-notifications \
  -p 5050:5050 \
  -e ConnectionStrings__GtsDatabase="Server=host.docker.internal;Port=3306;Database=gts;User=usuario;Password=password;" \
  -e PollingIntervalSeconds=2 \
  -e AllowedOrigin="http://localhost:3000" \
  -e ASPNETCORE_URLS="http://+:5050" \
  geocerca-notifications
```

> **Nota sobre el puerto:** Si cambias el puerto (ej: 8080), asegúrate de cambiar tanto `-p 8080:8080` como `-e ASPNETCORE_URLS="http://+:8080"`.

## Conexión desde el Frontend (JavaScript)

```bash
npm install @microsoft/signalr
```

```javascript
import * as signalR from "@microsoft/signalr";

const connection = new signalR.HubConnectionBuilder()
    .withUrl("http://localhost:5050/notificationsHub")
    .withAutomaticReconnect()
    .build();

// Escuchar alertas de geocerca
connection.on("GeofenceAlert", (payload) => {
    console.log("Alerta de geocerca recibida:", payload);
    // payload contiene:
    // {
    //   accountID, deviceID, geofenceID, geofenceName,
    //   eventType,   // "geofenceEnter" | "geofenceExit"
    //   serverTime, latitude, longitude,
    //   durationMinutes  // solo para geofenceExit (minutos dentro de la geocerca)
    // }
});

// Conectar e inscribirse al grupo de la cuenta
async function connect(accountID) {
    try {
        await connection.start();
        console.log("Conectado a SignalR");
        
        // Unirse al grupo de la cuenta para recibir sus alertas
        await connection.invoke("JoinAccountGroup", accountID);
        console.log(`Inscrito en el grupo de cuenta: ${accountID}`);
    } catch (err) {
        console.error("Error al conectar:", err);
        // withAutomaticReconnect se encarga de reintentar
    }
}

connect("MI_ACCOUNT_ID");
```

## Extensibilidad

Para agregar soporte a un nuevo tipo de evento (ej: alarma de velocidad):

1. Crea una nueva clase que implemente `IEventHandler`:

```csharp
public class SpeedAlarmEventHandler : IEventHandler
{
    public IReadOnlyList<string> SupportedEventTypes => new[] { "speedAlarm" };

    public async Task<bool> HandleAsync(DeviceEvent evt, CancellationToken ct)
    {
        // Construir payload, enviar por SignalR...
        return true;
    }
}
```

2. Regístrala en `Program.cs`:

```csharp
builder.Services.AddScoped<IEventHandler, SpeedAlarmEventHandler>();
```

Eso es todo. El `EventHandlerRegistry` lo descubrirá automáticamente y el `EventPollingService` incluirá `speedAlarm` en su cláusula IN del SELECT.

## Notas importantes

### Sobre la base de datos

- Este servicio es **de solo lectura** sobre `deviceevent`, excepto por el campo `isEnviado`.
- Solo ejecuta `SELECT` (para leer eventos pendientes) y `UPDATE ... SET isEnviado = 1 WHERE id = @Id` (para marcar eventos ya transmitidos).
- **No crea, modifica ni elimina ninguna tabla, columna ni otro dato.**

### Sobre `isEnviado`

> **⚠️ Importante:** El campo `isEnviado` representa únicamente si el motor de notificaciones ya intentó transmitir ese evento por SignalR. Es una responsabilidad 100% del backend.
>
> `isEnviado` **NO** representa si algún usuario específico ya vio o leyó esa notificación en su pantalla — ese es un concepto completamente distinto que se implementará en una etapa posterior, en una tabla separada ligada a cada usuario individual.
>
> **No reutilices `isEnviado` para rastrear lectura/vista por usuario.**
