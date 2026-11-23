using System.Collections.Concurrent;
using Dapper;
using GeocercaNotificationsService.Models;
using Microsoft.Data.Sqlite;

namespace GeocercaNotificationsService.Repositories;

/// <summary>
/// Implementación sobre SQLite embebido (modo WAL) con caché en memoria para las alertas activas.
/// </summary>
public sealed class ConfiguracionRepository : IConfiguracionRepository
{
    private readonly string _connectionString;
    private readonly ILogger<ConfiguracionRepository> _logger;

    // Caché en memoria: accountID -> (eventKey -> config de notificación activa)
    private readonly ConcurrentDictionary<string, Dictionary<string, AlertaNotificacionConfigDto>> _alertCache =
        new(StringComparer.OrdinalIgnoreCase);

    public ConfiguracionRepository(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<ConfiguracionRepository> logger)
    {
        _logger = logger;

        var configuredPath = configuration.GetValue<string>("ConfigDatabasePath");
        var dbPath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(environment.ContentRootPath, "data", "config.db")
            : configuredPath;

        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();

        InitializeSchema(dbPath);
    }

    private SqliteConnection CreateConnection() => new(_connectionString);

    private void InitializeSchema(string dbPath)
    {
        using var conn = CreateConnection();
        conn.Open();

        conn.Execute("""
            PRAGMA journal_mode = WAL;
            PRAGMA busy_timeout = 5000;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS cuenta_preferencias (
                account_id             TEXT PRIMARY KEY,
                tipo_mapa              TEXT    NOT NULL DEFAULT 'mapa',
                mostrar_placas         INTEGER NOT NULL DEFAULT 1,
                agrupar_unidades       INTEGER NOT NULL DEFAULT 0,
                seguir_unidad          INTEGER NOT NULL DEFAULT 1,
                silenciar_todo         INTEGER NOT NULL DEFAULT 0,
                volumen_general        INTEGER NOT NULL DEFAULT 100,
                no_molestar_activo     INTEGER NOT NULL DEFAULT 0,
                no_molestar_inicio     TEXT    NOT NULL DEFAULT '22:00',
                no_molestar_fin        TEXT    NOT NULL DEFAULT '07:00',
                velocidad_modo         TEXT    NOT NULL DEFAULT 'flota',
                velocidad_limite_flota INTEGER NOT NULL DEFAULT 90,
                updated_at             TEXT    NOT NULL
            );

            CREATE TABLE IF NOT EXISTS cuenta_alertas_config (
                account_id       TEXT    NOT NULL,
                event_key        TEXT    NOT NULL,
                sonido           TEXT    NOT NULL DEFAULT 'clasico',
                volumen          INTEGER NOT NULL DEFAULT 80,
                repetir_hasta_ok INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY (account_id, event_key)
            );

            CREATE TABLE IF NOT EXISTS cuenta_velocidad_vehiculos (
                account_id TEXT    NOT NULL,
                device_id  TEXT    NOT NULL,
                limite_kmh INTEGER NOT NULL,
                PRIMARY KEY (account_id, device_id)
            );

            CREATE TABLE IF NOT EXISTS cuenta_vehiculos_iconos (
                account_id TEXT NOT NULL,
                device_id  TEXT NOT NULL,
                icono      TEXT NOT NULL,
                PRIMARY KEY (account_id, device_id)
            );
            """);

        _logger.LogInformation("Base de datos SQLite de configuración inicializada en: {DbPath}", dbPath);
    }

    public async Task<ConfiguracionCuentaResponse> GetConfiguracionAsync(string accountID, CancellationToken ct = default)
    {
        var key = accountID.Trim();
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        var prefRow = await conn.QueryFirstOrDefaultAsync<CuentaPreferenciasRow>(
            new CommandDefinition(
                "SELECT * FROM cuenta_preferencias WHERE account_id = @AccountID COLLATE NOCASE LIMIT 1",
                new { AccountID = key }, cancellationToken: ct));

        var alertasRows = (await conn.QueryAsync<AlertaRow>(
            new CommandDefinition(
                "SELECT event_key, sonido, volumen, repetir_hasta_ok FROM cuenta_alertas_config WHERE account_id = @AccountID COLLATE NOCASE ORDER BY event_key",
                new { AccountID = key }, cancellationToken: ct))).ToList();

        var velRows = (await conn.QueryAsync<VelocidadVehiculoRow>(
            new CommandDefinition(
                "SELECT device_id, limite_kmh FROM cuenta_velocidad_vehiculos WHERE account_id = @AccountID COLLATE NOCASE ORDER BY device_id",
                new { AccountID = key }, cancellationToken: ct))).ToList();

        var iconoRows = (await conn.QueryAsync<IconoVehiculoRow>(
            new CommandDefinition(
                "SELECT device_id, icono FROM cuenta_vehiculos_iconos WHERE account_id = @AccountID COLLATE NOCASE ORDER BY device_id",
                new { AccountID = key }, cancellationToken: ct))).ToList();

        var response = new ConfiguracionCuentaResponse
        {
            AccountID = key,
            UpdatedAt = prefRow?.UpdatedAt
        };

        if (prefRow is not null)
        {
            response.Preferencias = new PreferenciasVisualizacionDto
            {
                TipoMapa = prefRow.TipoMapa,
                MostrarPlacas = prefRow.MostrarPlacas != 0,
                AgruparUnidades = prefRow.AgruparUnidades != 0,
                SeguirUnidad = prefRow.SeguirUnidad != 0
            };

            response.SonidoGlobal = new SonidoGlobalDto
            {
                SilenciarTodo = prefRow.SilenciarTodo != 0,
                VolumenGeneral = prefRow.VolumenGeneral,
                NoMolestarActivo = prefRow.NoMolestarActivo != 0,
                NoMolestarInicio = prefRow.NoMolestarInicio,
                NoMolestarFin = prefRow.NoMolestarFin
            };

            response.LimitesVelocidad = new LimitesVelocidadDto
            {
                Modo = prefRow.VelocidadModo,
                LimiteFlotaKmh = prefRow.VelocidadLimiteFlota
            };
        }

        response.Notificaciones = alertasRows.Select(r => new AlertaNotificacionConfigDto
        {
            EventKey = r.EventKey,
            Sonido = r.Sonido,
            Volumen = r.Volumen,
            RepetirHastaOk = r.RepetirHastaOk != 0
        }).ToList();

        foreach (var v in velRows)
        {
            response.LimitesVelocidad.PorVehiculo[v.DeviceId] = v.LimiteKmh;
        }

        foreach (var i in iconoRows)
        {
            response.Iconos[i.DeviceId] = i.Icono;
        }

        return response;
    }

    public async Task<AlertaNotificacionConfigDto?> GetActiveAlertConfigAsync(
        string accountID,
        string eventKey,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accountID) || string.IsNullOrWhiteSpace(eventKey))
            return null;

        var key = accountID.Trim();

        if (!_alertCache.TryGetValue(key, out var map))
        {
            await using var conn = CreateConnection();
            await conn.OpenAsync(ct);

            var rows = await conn.QueryAsync<AlertaRow>(
                new CommandDefinition(
                    "SELECT event_key, sonido, volumen, repetir_hasta_ok FROM cuenta_alertas_config WHERE account_id = @AccountID COLLATE NOCASE",
                    new { AccountID = key }, cancellationToken: ct));

            map = new Dictionary<string, AlertaNotificacionConfigDto>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
            {
                map[r.EventKey] = new AlertaNotificacionConfigDto
                {
                    EventKey = r.EventKey,
                    Sonido = r.Sonido,
                    Volumen = r.Volumen,
                    RepetirHastaOk = r.RepetirHastaOk != 0
                };
            }

            _alertCache[key] = map;
        }

        return map.TryGetValue(eventKey, out var cfg) ? cfg : null;
    }

    public async Task<PreferenciasVisualizacionDto> SavePreferenciasAsync(
        string accountID,
        PreferenciasVisualizacionDto dto,
        CancellationToken ct = default)
    {
        var key = accountID.Trim();
        var tipoMapa = NormalizeMapType(dto.TipoMapa);
        var now = DateTime.UtcNow.ToString("o");

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await EnsurePreferenciasRowExistsAsync(conn, null, key, now, ct);

        await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE cuenta_preferencias
            SET tipo_mapa        = @TipoMapa,
                mostrar_placas   = @MostrarPlacas,
                agrupar_unidades = @AgruparUnidades,
                seguir_unidad    = @SeguirUnidad,
                updated_at       = @UpdatedAt
            WHERE account_id = @AccountID COLLATE NOCASE
            """, new
        {
            AccountID = key,
            TipoMapa = tipoMapa,
            MostrarPlacas = dto.MostrarPlacas ? 1 : 0,
            AgruparUnidades = dto.AgruparUnidades ? 1 : 0,
            SeguirUnidad = dto.SeguirUnidad ? 1 : 0,
            UpdatedAt = now
        }, cancellationToken: ct));

        return new PreferenciasVisualizacionDto
        {
            TipoMapa = tipoMapa,
            MostrarPlacas = dto.MostrarPlacas,
            AgruparUnidades = dto.AgruparUnidades,
            SeguirUnidad = dto.SeguirUnidad
        };
    }

    public async Task<GuardarAlertasConfigRequest> SaveAlertasAsync(
        string accountID,
        GuardarAlertasConfigRequest request,
        CancellationToken ct = default)
    {
        var key = accountID.Trim();
        var now = DateTime.UtcNow.ToString("o");
        var sg = request.SonidoGlobal ?? new SonidoGlobalDto();

        var volumenGeneral = Math.Clamp(sg.VolumenGeneral, 0, 100);
        var inicio = string.IsNullOrWhiteSpace(sg.NoMolestarInicio) ? "22:00" : sg.NoMolestarInicio.Trim();
        var fin = string.IsNullOrWhiteSpace(sg.NoMolestarFin) ? "07:00" : sg.NoMolestarFin.Trim();

        // Deduplicar y normalizar lista de notificaciones
        var dedup = new Dictionary<string, AlertaNotificacionConfigDto>(StringComparer.OrdinalIgnoreCase);
        if (request.Notificaciones is not null)
        {
            foreach (var n in request.Notificaciones)
            {
                if (string.IsNullOrWhiteSpace(n.EventKey)) continue;
                var canonicalKey = AlertEventCatalog.GetCanonicalEventKey(n.EventKey);
                dedup[canonicalKey] = new AlertaNotificacionConfigDto
                {
                    EventKey = canonicalKey,
                    Sonido = string.IsNullOrWhiteSpace(n.Sonido) ? "clasico" : n.Sonido.Trim(),
                    Volumen = Math.Clamp(n.Volumen, 0, 100),
                    RepetirHastaOk = n.RepetirHastaOk
                };
            }
        }

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await EnsurePreferenciasRowExistsAsync(conn, tx, key, now, ct);

        await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE cuenta_preferencias
            SET silenciar_todo     = @SilenciarTodo,
                volumen_general    = @VolumenGeneral,
                no_molestar_activo = @NoMolestarActivo,
                no_molestar_inicio = @NoMolestarInicio,
                no_molestar_fin    = @NoMolestarFin,
                updated_at         = @UpdatedAt
            WHERE account_id = @AccountID COLLATE NOCASE
            """, new
        {
            AccountID = key,
            SilenciarTodo = sg.SilenciarTodo ? 1 : 0,
            VolumenGeneral = volumenGeneral,
            NoMolestarActivo = sg.NoMolestarActivo ? 1 : 0,
            NoMolestarInicio = inicio,
            NoMolestarFin = fin,
            UpdatedAt = now
        }, transaction: tx, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM cuenta_alertas_config WHERE account_id = @AccountID COLLATE NOCASE",
            new { AccountID = key }, transaction: tx, cancellationToken: ct));

        foreach (var item in dedup.Values)
        {
            await conn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO cuenta_alertas_config (account_id, event_key, sonido, volumen, repetir_hasta_ok)
                VALUES (@AccountID, @EventKey, @Sonido, @Volumen, @RepetirHastaOk)
                """, new
            {
                AccountID = key,
                item.EventKey,
                item.Sonido,
                item.Volumen,
                RepetirHastaOk = item.RepetirHastaOk ? 1 : 0
            }, transaction: tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);

        // Actualizar caché en memoria inmediatamente
        _alertCache[key] = dedup;

        return new GuardarAlertasConfigRequest
        {
            SonidoGlobal = new SonidoGlobalDto
            {
                SilenciarTodo = sg.SilenciarTodo,
                VolumenGeneral = volumenGeneral,
                NoMolestarActivo = sg.NoMolestarActivo,
                NoMolestarInicio = inicio,
                NoMolestarFin = fin
            },
            Notificaciones = dedup.Values.ToList()
        };
    }

    public async Task<LimitesVelocidadDto> SaveLimitesVelocidadAsync(
        string accountID,
        LimitesVelocidadDto dto,
        CancellationToken ct = default)
    {
        var key = accountID.Trim();
        var now = DateTime.UtcNow.ToString("o");
        var modo = string.Equals(dto.Modo, "vehiculo", StringComparison.OrdinalIgnoreCase) ? "vehiculo" : "flota";
        var limiteFlota = Math.Clamp(dto.LimiteFlotaKmh, 1, 300);

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await EnsurePreferenciasRowExistsAsync(conn, tx, key, now, ct);

        await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE cuenta_preferencias
            SET velocidad_modo         = @Modo,
                velocidad_limite_flota = @LimiteFlota,
                updated_at             = @UpdatedAt
            WHERE account_id = @AccountID COLLATE NOCASE
            """, new
        {
            AccountID = key,
            Modo = modo,
            LimiteFlota = limiteFlota,
            UpdatedAt = now
        }, transaction: tx, cancellationToken: ct));

        var cleanedVehiculos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        if (dto.PorVehiculo is not null)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM cuenta_velocidad_vehiculos WHERE account_id = @AccountID COLLATE NOCASE",
                new { AccountID = key }, transaction: tx, cancellationToken: ct));

            foreach (var (deviceId, limite) in dto.PorVehiculo)
            {
                if (string.IsNullOrWhiteSpace(deviceId)) continue;
                var dev = deviceId.Trim();
                var clamped = Math.Clamp(limite, 1, 300);
                cleanedVehiculos[dev] = clamped;

                await conn.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO cuenta_velocidad_vehiculos (account_id, device_id, limite_kmh)
                    VALUES (@AccountID, @DeviceID, @LimiteKmh)
                    """, new
                {
                    AccountID = key,
                    DeviceID = dev,
                    LimiteKmh = clamped
                }, transaction: tx, cancellationToken: ct));
            }
        }

        await tx.CommitAsync(ct);

        return new LimitesVelocidadDto
        {
            Modo = modo,
            LimiteFlotaKmh = limiteFlota,
            PorVehiculo = cleanedVehiculos
        };
    }

    public async Task<Dictionary<string, string>> SaveIconosAsync(
        string accountID,
        GuardarIconosRequest request,
        CancellationToken ct = default)
    {
        var key = accountID.Trim();
        var now = DateTime.UtcNow.ToString("o");

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        await EnsurePreferenciasRowExistsAsync(conn, tx, key, now, ct);

        if (request.ReemplazarTodo)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM cuenta_vehiculos_iconos WHERE account_id = @AccountID COLLATE NOCASE",
                new { AccountID = key }, transaction: tx, cancellationToken: ct));
        }

        if (request.Iconos is not null)
        {
            foreach (var (deviceId, rawIcono) in request.Iconos)
            {
                if (string.IsNullOrWhiteSpace(deviceId)) continue;
                var dev = deviceId.Trim();
                var icono = rawIcono?.Trim().ToLowerInvariant();

                // "auto" es el valor por defecto: eliminamos la fila para mantener la tabla limpia
                if (string.IsNullOrWhiteSpace(icono) || icono == "auto")
                {
                    await conn.ExecuteAsync(new CommandDefinition(
                        "DELETE FROM cuenta_vehiculos_iconos WHERE account_id = @AccountID COLLATE NOCASE AND device_id = @DeviceID COLLATE NOCASE",
                        new { AccountID = key, DeviceID = dev }, transaction: tx, cancellationToken: ct));
                }
                else
                {
                    await conn.ExecuteAsync(new CommandDefinition("""
                        INSERT INTO cuenta_vehiculos_iconos (account_id, device_id, icono)
                        VALUES (@AccountID, @DeviceID, @Icono)
                        ON CONFLICT(account_id, device_id) DO UPDATE SET icono = excluded.icono
                        """, new
                    {
                        AccountID = key,
                        DeviceID = dev,
                        Icono = icono
                    }, transaction: tx, cancellationToken: ct));
                }
            }
        }

        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE cuenta_preferencias SET updated_at = @UpdatedAt WHERE account_id = @AccountID COLLATE NOCASE",
            new { AccountID = key, UpdatedAt = now }, transaction: tx, cancellationToken: ct));

        await tx.CommitAsync(ct);

        var rows = await conn.QueryAsync<IconoVehiculoRow>(
            new CommandDefinition(
                "SELECT device_id, icono FROM cuenta_vehiculos_iconos WHERE account_id = @AccountID COLLATE NOCASE ORDER BY device_id",
                new { AccountID = key }, cancellationToken: ct));

        return rows.ToDictionary(r => r.DeviceId, r => r.Icono, StringComparer.OrdinalIgnoreCase);
    }

    public async Task ResetIconosAsync(string accountID, CancellationToken ct = default)
    {
        var key = accountID.Trim();
        var now = DateTime.UtcNow.ToString("o");

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM cuenta_vehiculos_iconos WHERE account_id = @AccountID COLLATE NOCASE",
            new { AccountID = key }, cancellationToken: ct));

        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE cuenta_preferencias SET updated_at = @UpdatedAt WHERE account_id = @AccountID COLLATE NOCASE",
            new { AccountID = key, UpdatedAt = now }, cancellationToken: ct));
    }

    private static async Task EnsurePreferenciasRowExistsAsync(
        SqliteConnection conn,
        System.Data.Common.DbTransaction? tx,
        string accountID,
        string now,
        CancellationToken ct)
    {
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO cuenta_preferencias (account_id, updated_at)
            SELECT @AccountID, @UpdatedAt
            WHERE NOT EXISTS (
                SELECT 1 FROM cuenta_preferencias WHERE account_id = @AccountID COLLATE NOCASE
            )
            """, new { AccountID = accountID, UpdatedAt = now }, transaction: tx, cancellationToken: ct));
    }

    private static string NormalizeMapType(string? tipoMapa)
    {
        return tipoMapa?.Trim().ToLowerInvariant() switch
        {
            "satelite" or "satellite" => "satelite",
            "hibrido" or "hybrid" => "hibrido",
            "relieve" or "terrain" => "relieve",
            _ => "mapa"
        };
    }

    // ── Filas internas de mapeo SQLite ────────────────────────────────────────
    private sealed class CuentaPreferenciasRow
    {
        public string AccountId { get; set; } = string.Empty;
        public string TipoMapa { get; set; } = "mapa";
        public int MostrarPlacas { get; set; } = 1;
        public int AgruparUnidades { get; set; }
        public int SeguirUnidad { get; set; } = 1;
        public int SilenciarTodo { get; set; }
        public int VolumenGeneral { get; set; } = 100;
        public int NoMolestarActivo { get; set; }
        public string NoMolestarInicio { get; set; } = "22:00";
        public string NoMolestarFin { get; set; } = "07:00";
        public string VelocidadModo { get; set; } = "flota";
        public int VelocidadLimiteFlota { get; set; } = 90;
        public string UpdatedAt { get; set; } = string.Empty;
    }

    private sealed class AlertaRow
    {
        public string EventKey { get; set; } = string.Empty;
        public string Sonido { get; set; } = "clasico";
        public int Volumen { get; set; } = 80;
        public int RepetirHastaOk { get; set; }
    }

    private sealed class VelocidadVehiculoRow
    {
        public string DeviceId { get; set; } = string.Empty;
        public int LimiteKmh { get; set; }
    }

    private sealed class IconoVehiculoRow
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Icono { get; set; } = string.Empty;
    }
}
