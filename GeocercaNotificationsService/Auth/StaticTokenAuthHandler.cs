using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GeocercaNotificationsService.Auth;

/// <summary>
/// Manejador de autenticación que valida el token estático permanente
/// o tokens JWT de la aplicación.
/// </summary>
public sealed class StaticTokenAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IConfiguration _configuration;

    public StaticTokenAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration) : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? token = null;

        // 1. Obtener de header Authorization: Bearer <token>
        if (Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var headerVal = authHeader.ToString().Trim();
            if (headerVal.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                token = headerVal.Substring("Bearer ".Length).Trim();
            }
            else
            {
                token = headerVal;
            }
        }

        // 2. O de header alternativo X-API-KEY
        if (string.IsNullOrWhiteSpace(token) && Request.Headers.TryGetValue("X-API-KEY", out var apiKeyHeader))
        {
            token = apiKeyHeader.ToString().Trim();
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult(AuthenticateResult.Fail("Token de autorización no provisto."));
        }

        var expectedStaticToken = _configuration.GetValue<string>("StaticApiToken")
                                  ?? "trackvel-static-token-2026";

        // Validación con token estático permanente
        if (string.Equals(token, expectedStaticToken, StringComparison.Ordinal))
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "StaticClient"),
                new Claim("TokenType", "Static")
            };
            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        // Validación opcional con JWT si se proveyó uno
        try
        {
            var secretKey = _configuration.GetValue<string>("settings:secretkey");
            if (!string.IsNullOrWhiteSpace(secretKey))
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var keyBytes = Encoding.UTF8.GetBytes(secretKey);
                var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
                    ValidateIssuer = false,
                    ValidateAudience = false
                }, out _);

                var ticket = new AuthenticationTicket(principal, Scheme.Name);
                return Task.FromResult(AuthenticateResult.Success(ticket));
            }
        }
        catch
        {
            // Falló la validación JWT
        }

        return Task.FromResult(AuthenticateResult.Fail("Token de autorización inválido."));
    }
}
