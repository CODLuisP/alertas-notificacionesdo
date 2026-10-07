using System.Reflection;
using Dapper;
using GeocercaNotificationsService.Auth;
using GeocercaNotificationsService.Handlers;
using GeocercaNotificationsService.Hubs;
using GeocercaNotificationsService.Repositories;
using GeocercaNotificationsService.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi.Models;

// Permitir mapeo automático de columnas con guion bajo (snake_case a PascalCase en Dapper)
DefaultTypeMap.MatchNamesWithUnderscores = true;

var builder = WebApplication.CreateBuilder(args);

// ── 1. Configuración de Proxy Inverso (Nginx en DigitalOcean) ─────────────────
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ── 2. Controladores ──────────────────────────────────────────────────────────
builder.Services.AddControllers();

// ── 3. Repositorios ───────────────────────────────────────────────────────────
builder.Services.AddScoped<IVisitasGeocercaRepository, VisitasGeocercaRepository>();

// ── 4. SignalR ────────────────────────────────────────────────────────────────
builder.Services.AddSignalR();

// ── 5. Handlers de eventos ───────────────────────────────────────────────────
builder.Services.AddScoped<IEventHandler, GeofenceEventHandler>();
builder.Services.AddScoped<EventHandlerRegistry>();

// ── 6. Background services de sondeo ─────────────────────────────────────────
builder.Services.AddHostedService<EventPollingService>();

// ── 7. WhatsApp (Cliente HTTP, Lógica de Negocio y Worker Independiente) ───────
builder.Services.AddHttpClient<IWhatsAppClient, WhatsAppClient>();
builder.Services.AddScoped<IWhatsAppNotificationService, WhatsAppNotificationService>();
builder.Services.AddHostedService<WhatsAppPollingService>();

// ── 8. Autenticación con Token Estático Permanente (y fallback JWT) ───────────
builder.Services.AddAuthentication("StaticToken")
    .AddScheme<AuthenticationSchemeOptions, StaticTokenAuthHandler>("StaticToken", null);

builder.Services.AddAuthorization();

// ── 9. Swagger / OpenAPI con autenticación Bearer ─────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Geocerca Notifications & Reports Service",
        Version = "v1",
        Description = "Microservicio para alertas de geocerca en tiempo real (SignalR), notificaciones WhatsApp y reportes de visitas (visitas_geocerca)."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Description = "Ingrese el token estático permanente (por defecto: 'trackvel-static-token-2026'). Swagger antepondrá 'Bearer ' automáticamente.",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "StaticToken"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
    }
});

// ── 10. CORS configurable por variable de entorno ─────────────────────────────
var allowedOrigins = builder.Configuration.GetValue<string>("AllowedOrigins")
    ?? builder.Configuration.GetValue<string>("AllowedOrigin");

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (string.IsNullOrWhiteSpace(allowedOrigins) || allowedOrigins.Trim() == "*")
        {
            policy.SetIsOriginAllowed(_ => true);
        }
        else
        {
            var origins = allowedOrigins
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();
            policy.WithOrigins(origins);
        }

        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Requerido por SignalR WebSockets
    });
});

var app = builder.Build();

// ── Middleware Pipeline ──────────────────────────────────────────────────────

// Headers del proxy inverso (X-Forwarded-For, X-Forwarded-Proto) DEBE ir primero
app.UseForwardedHeaders();

app.UseCors();

// Swagger UI: configurable vía variable EnableSwagger (default: true).
// Usa ruta relativa 'v1/swagger.json' para resolver correctamente tanto en localhost
// como detrás del subpath /notificaciones-trackbell/ de Nginx en DigitalOcean.
var enableSwagger = app.Configuration.GetValue("EnableSwagger", true);
if (enableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("v1/swagger.json", "Geocerca Service v1");
        c.RoutePrefix = "swagger";
        c.DocumentTitle = "Geocerca Service - Swagger API Documentation";
    });
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<NotificationsHub>("/notificationsHub");

// Redirecciones relativas (para funcionar dentro o fuera del subpath de Nginx)
app.MapGet("/", () => Results.Redirect("swagger/index.html"));
app.MapGet("/swagger", () => Results.Redirect("swagger/index.html"));

// Monitor web de pruebas (soporta ubicación local /app y raíz ../)
app.MapGet("/monitor", (IWebHostEnvironment env) =>
{
    var localPath = Path.Combine(env.ContentRootPath, "test-signalr.html");
    var parentPath = Path.Combine(env.ContentRootPath, "..", "test-signalr.html");
    var targetPath = File.Exists(localPath) ? localPath : parentPath;

    return File.Exists(targetPath)
        ? Results.Content(File.ReadAllText(targetPath), "text/html; charset=utf-8")
        : Results.NotFound("test-signalr.html no encontrado");
});

// Endpoint de salud para Docker Healthcheck y monitor de DigitalOcean
app.MapGet("/health", () => Results.Ok(new
{
    service = "GeocercaNotificationsService",
    status = "running",
    timestamp = DateTime.UtcNow
}));

app.Run();
