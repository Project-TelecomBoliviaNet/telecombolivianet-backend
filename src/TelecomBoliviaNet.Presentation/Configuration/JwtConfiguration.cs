using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace TelecomBoliviaNet.Presentation.Configuration;

public static class JwtConfiguration
{
    public static IServiceCollection AddJwtAuthentication(
        this IServiceCollection services, IConfiguration config)
    {
        var key = config["Jwt:Key"]
            ?? throw new InvalidOperationException(
                "Jwt:Key no configurado en appsettings.");

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme    = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer           = true,
                    ValidateAudience         = true,
                    ValidateLifetime         = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer              = config["Jwt:Issuer"],
                    ValidAudience            = config["Jwt:Audience"],
                    IssuerSigningKey         = new SymmetricSecurityKey(
                                                Encoding.UTF8.GetBytes(key)),
                    ClockSkew                = TimeSpan.Zero
                };

                // ── SignalR: leer JWT desde query string ──────────────────────
                // El cliente JS de SignalR no puede inyectar headers en la
                // negociación WebSocket — envía el token como ?access_token=...
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ctx =>
                    {
                        var accessToken = ctx.Request.Query["access_token"];
                        var path        = ctx.HttpContext.Request.Path;

                        // Solo aplica para el Hub de SignalR
                        if (!string.IsNullOrEmpty(accessToken) &&
                            path.StartsWithSegments("/hubs"))
                        {
                            ctx.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    },

                    OnChallenge = ctx =>
                    {
                        ctx.HandleResponse();
                        ctx.Response.StatusCode  = 401;
                        ctx.Response.ContentType = "application/json";
                        return ctx.Response.WriteAsJsonAsync(new
                        {
                            message = "No autenticado. Se requiere un token válido."
                        });
                    },

                    OnForbidden = ctx =>
                    {
                        ctx.Response.StatusCode  = 403;
                        ctx.Response.ContentType = "application/json";
                        return ctx.Response.WriteAsJsonAsync(new
                        {
                            message = "No tienes permisos para realizar esta acción."
                        });
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy("AdminOnly",        p => p.RequireRole("Admin"))
            .AddPolicy("AdminOrTecnico",   p => p.RequireRole("Admin", "Tecnico"))
            // US-ROL-CRUD: nueva política para el rol Operador
            .AddPolicy("AdminOrOperador",  p => p.RequireRole("Admin", "Operador"))
            .AddPolicy("AllRoles",         p => p.RequireRole("Admin", "Operador", "Tecnico", "SocioLectura"));

        return services;
    }
}
