# Guía de Despliegue en DigitalOcean (Docker + Nginx)

Esta guía explica paso a paso cómo desplegar el microservicio **`notificaciones-trackbell`** en un Droplet de DigitalOcean (Ubuntu) con Docker, Docker Compose y Nginx como Reverse Proxy con terminación SSL.

---

## 1. Arquitectura del Despliegue

* **Contenedor Docker**: Corre la app .NET 8 escuchando en el puerto interno `8080` con usuario no-root (`app`) y zona horaria `America/Lima`.
* **Puerto en el Host**: `127.0.0.1:3008` (publicado **exclusivamente** en localhost, inaccesible desde el internet público sin pasar por Nginx).
* **Nginx Reverse Proxy**:
  * Recibe peticiones HTTPS en `https://do.velsat.pe:8443/notificaciones-trackbell/`
  * Termina el SSL (Let's Encrypt / Cloudflare)
  * Quita el prefijo `/notificaciones-trackbell/` y redirige el tráfico a `http://127.0.0.1:3008/`
  * Maneja WebSockets (SignalR) con encabezados `Upgrade` y `Connection`.

---

## 2. Preparación en el Servidor (DigitalOcean)

### Paso 1: Clonar el Repositorio
Conéctate por SSH a tu servidor y clona el proyecto en la carpeta deseada (ejemplo: `/opt/notificaciones-trackbell` o `/var/www/notificaciones-trackbell`):

```bash
cd /opt
git clone https://github.com/TU_USUARIO/notificaciones-trackbell.git
cd notificaciones-trackbell
```

### Paso 2: Crear el archivo `.env`
Copia la plantilla `.env.example` para crear tu `.env` de producción:

```bash
cp .env.example .env
nano .env
```

### Paso 3: Configurar las variables en `.env`

| Variable | Descripción / Ejemplo |
| :--- | :--- |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `ASPNETCORE_HTTP_PORTS` | `8080` (puerto dentro del contenedor) |
| `EnableSwagger` | `true` (mantiene Swagger UI activo en producción) |
| `PORT_HOST` | `3008` (puerto expuesto en 127.0.0.1) |
| `ConnectionStrings__GtsDatabase` | Cadena de conexión MySQL (ver sección de base de datos abajo) |
| `StaticApiToken` | `trackvel-static-token-2026` (token permanente de la API) |
| `settings__secretkey` | Clave secreta para JWT |
| `PollingIntervalSeconds` | `2` (intervalo de sondeo de base de datos en segundos) |
| `AllowedOrigins` | `*` o lista separada por comas: `https://do.velsat.pe:8443,https://trackvel.pe` |
| `WhatsApp__Enabled` | `true` (activa el canal de notificaciones WhatsApp) |
| `WhatsApp__ApiUrl` | `https://do.velsat.pe:8443/whatsapp/api/send/single` |
| `WhatsApp__ApiKey` | Tu API Key real de WhatsApp |
| `WhatsApp__TimeoutMs` | `10000` |
| `WhatsApp__DelayBetweenSendsMs` | `400` |

#### ⚠️ Conexión a MySQL en el mismo Droplet:
* Si MySQL corre **en el mismo Droplet (directamente en Ubuntu)**:
  ```env
  ConnectionStrings__GtsDatabase=Server=host.docker.internal;Port=3306;Database=gts;Uid=velsatuser;Password=TU_PASSWORD;Charset=utf8mb4;Pooling=true;
  ```
  *(Asegúrate de que en `/etc/mysql/mysql.conf.d/mysqld.cnf` el parámetro `bind-address` sea `0.0.0.0` o permita conexiones desde la interfaz `docker0`).*
* Si MySQL corre en una **IP o servidor externo** (ejemplo `66.240.210.125`):
  ```env
  ConnectionStrings__GtsDatabase=Server=66.240.210.125;Port=3306;Database=gts;Uid=velsatuser;Password=TU_PASSWORD;Charset=utf8mb4;Pooling=true;
  ```

---

## 3. Levantar el Contenedor con Docker Compose

Ejecuta la construcción de la imagen y levanta el servicio en segundo plano:

```bash
docker compose up -d --build
```

### Verificar Estado y Salud:
```bash
# Ver estado del contenedor y healthcheck
docker compose ps

# Probar el endpoint de salud directamente en localhost del host
curl http://127.0.0.1:3008/health
```

Deberías recibir una respuesta JSON:
```json
{"service":"GeocercaNotificationsService","status":"running","timestamp":"..."}
```

---

## 4. Configuración del Bloque Nginx

Agrega o verifica este bloque dentro de tu configuración de Nginx en `/etc/nginx/sites-available/...`:

```nginx
location /notificaciones-trackbell/ {
    # La barra al final de http://localhost:3008/ quita el prefijo /notificaciones-trackbell/
    proxy_pass http://localhost:3008/;
    proxy_http_version 1.1;
    
    # Soporte para WebSockets (SignalR)
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    
    # Encabezados de proxy inverso
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    proxy_set_header X-Forwarded-Proto $scheme;
    proxy_set_header X-Real-IP $remote_addr;
    
    # Timeouts extendidos para conexiones persistentes de SignalR
    proxy_read_timeout 3600s;
    proxy_send_timeout 3600s;
}
```

Aplica los cambios en Nginx:
```bash
sudo nginx -t && sudo systemctl reload nginx
```

---

## 5. URLs Públicas de Acceso

Una vez levantado Nginx y el contenedor:

* **Endpoint de Salud**:
  `https://do.velsat.pe:8443/notificaciones-trackbell/health`
* **Swagger UI / Documentación**:
  `https://do.velsat.pe:8443/notificaciones-trackbell/swagger`
* **Hub de SignalR (WebSockets)**:
  `https://do.velsat.pe:8443/notificaciones-trackbell/notificationsHub`
* **Monitor Web de Pruebas**:
  `https://do.velsat.pe:8443/notificaciones-trackbell/monitor`
* **APIs de Configuración WhatsApp**:
  `https://do.velsat.pe:8443/notificaciones-trackbell/api/geocercas/{geofenceID}/whatsapp`

---

## 6. Mantenimiento y Comandos Frecuentes

### Ver Logs en Tiempo Real:
```bash
# Ver los últimos 100 logs y seguir en vivo
docker compose logs -f --tail=100
```

### Reiniciar el Contenedor:
```bash
docker compose restart
```

### Actualizar con Nuevos Cambios de GitHub:
```bash
git pull origin main
docker compose up -d --build
```

### Detener el Servicio:
```bash
docker compose down
```
