using Serilog;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Infrastructure.Data;
using TelecomBoliviaNet.Presentation.Configuration;
using TelecomBoliviaNet.Presentation.Middleware;
using TelecomBoliviaNet.Presentation.Hubs;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Iniciando TelecomBoliviaNet API...");

    var builder = WebApplication.CreateBuilder(args);

    // ── Configuración en runtime (appsettings guardados por AdminSettingsService) ─
    // Se carga DESPUÉS de appsettings.json y appsettings.{env}.json para sobreescribir.
    // El archivo se crea/actualiza en PUT /api/admin/settings sin necesitar reinicio.
    var runtimeSettingsPath = Path.Combine(builder.Environment.ContentRootPath, "appsettings.runtime.json");
    if (!File.Exists(runtimeSettingsPath))
        File.WriteAllText(runtimeSettingsPath, "{}");  // archivo vacío para arranque limpio
    builder.Configuration.AddJsonFile("appsettings.runtime.json",
        optional: true, reloadOnChange: true);

    // ── Serilog ───────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((ctx, services, config) =>
        config.ReadFrom.Configuration(ctx.Configuration)
              .ReadFrom.Services(services)
              .Enrich.FromLogContext()
              .WriteTo.Console());

    // ── Validación de seguridad: JWT key requerida en todos los entornos ────────
    // Fail-fast: la app no arranca si la key está ausente o es demasiado corta.
    // En desarrollo proveer en appsettings.Development.json; en producción en variables de entorno.
    {
        var jwtKey = builder.Configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
            throw new InvalidOperationException(
                "Jwt:Key debe tener al menos 32 caracteres. " +
                "En desarrollo: appsettings.Development.json. " +
                "En producción: variable de entorno Jwt__Key o dotnet user-secrets.");
    }

    // ── Todos los servicios de aplicación (DB, repos, servicios, validadores) ─
    builder.Services.AddApplicationServices(builder.Configuration);

    // ── FIX-20: manejo global de excepciones — formato ProblemDetails consistente ─
    builder.Services.AddExceptionHandler<TelecomBoliviaNet.Presentation.Middleware.GlobalExceptionHandler>();
    builder.Services.AddProblemDetails();

    // ── Memory Cache (usado por TokenBlacklistMiddleware para reducir queries a BD) ─
    builder.Services.AddMemoryCache();

    // ── JWT + Políticas de autorización ───────────────────────────────────────
    builder.Services.AddJwtAuthentication(builder.Configuration);

    // ── Rate Limiting (ASP.NET 8 nativo) ──────────────────────────────────────
    // Protege endpoints críticos: login (10/5min), webhook (100/min), API general (300/min)
    builder.Services.AddRateLimiting();

    // ── SignalR ───────────────────────────────────────────────────────────────
    // Configura SignalR con soporte para JWT en la query string (necesario
    // para que el cliente JS pueda autenticarse en la negociación del Hub).
    builder.Services.AddSignalR(options =>
    {
        options.EnableDetailedErrors = builder.Environment.IsDevelopment();
        options.KeepAliveInterval    = TimeSpan.FromSeconds(15);
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    });

    // ── CORS ──────────────────────────────────────────────────────────────────
    var allowedOrigins = builder.Configuration
        .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

    builder.Services.AddCors(options =>
        options.AddPolicy("Frontend", policy =>
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  // REQUERIDO para SignalR: permite credenciales (cookies/auth)
                  .AllowCredentials()));

    // ── FIX-29: API versioning ────────────────────────────────────────────────
    // AssumeDefaultVersionWhenUnspecified = true: las rutas sin prefijo /v1/ siguen funcionando.
    // ReportApiVersions = true: agrega cabeceras api-supported-versions y api-deprecated-versions.
    builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new Asp.Versioning.ApiVersion(1, 0);
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ReportApiVersions = true;
    }).AddMvc();

    // ── Controllers + JSON ────────────────────────────────────────────────────
    builder.Services
        .AddControllers()
        .AddJsonOptions(o =>
        {
            o.JsonSerializerOptions.PropertyNamingPolicy = null; // PascalCase
            o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });

    // ── Swagger ───────────────────────────────────────────────────────────────
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwagger();

    var app = builder.Build();

    // ── Migraciones automáticas al iniciar ────────────────────────────────────
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Log.Information("Verificando migraciones de base de datos...");

        try
        {
            // MigrateAsync() es idempotente: aplica solo las pendientes y no hace nada si ya están al día.
            await db.Database.MigrateAsync();
            var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
            Log.Information("Migraciones al día. Total en DB: {Count}", applied.Count);
        }
        catch (Exception exMigrate)
        {
            Log.Fatal(exMigrate,
                "ERROR CRÍTICO en migración — revisa la cadena de conexión y permisos de la BD.");
            throw;
        }

        // ── Seed data (idempotente — solo inserta si no existe) ───────────────
        if (!await db.UserSystems.AnyAsync())
        {
            Log.Information("Insertando datos iniciales (admin, planes, secuencia TBN)...");

            db.UserSystems.Add(new TelecomBoliviaNet.Domain.Entities.Auth.UserSystem
            {
                Id                     = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                FullName               = "Administrador del Sistema",
                Email                  = "admin@telecombolivianet.bo",
                PasswordHash           = "$2a$12$SKw6qCQwOINZMk.BN1AZNuuTskGw0nXNetQ0h9paT8ajYzvkTa.vy",
                Role                   = TelecomBoliviaNet.Domain.Entities.Auth.UserRole.Admin,
                Status                 = TelecomBoliviaNet.Domain.Entities.Auth.UserStatus.Activo,
                RequiresPasswordChange = true,
                FailedLoginAttempts    = 0,
                CreatedAt              = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

            if (!await db.TbnSequences.AnyAsync())
            {
                db.TbnSequences.Add(new TelecomBoliviaNet.Domain.Entities.Clients.TbnSequence
                {
                    Id        = 1,
                    LastValue = 0,
                    Prefix    = "TBN"
                });
            }

            if (!await db.Plans.AnyAsync())
            {
                db.Plans.AddRange(
                    new TelecomBoliviaNet.Domain.Entities.Plans.Plan
                    {
                        Id = Guid.Parse("00000000-0000-0000-0001-000000000001"),
                        Name = "Plan Cobre", SpeedMb = 30, MonthlyPrice = 99.00m,
                        IsActive = true, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    },
                    new TelecomBoliviaNet.Domain.Entities.Plans.Plan
                    {
                        Id = Guid.Parse("00000000-0000-0000-0001-000000000002"),
                        Name = "Plan Plata", SpeedMb = 50, MonthlyPrice = 149.00m,
                        IsActive = true, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    },
                    new TelecomBoliviaNet.Domain.Entities.Plans.Plan
                    {
                        Id = Guid.Parse("00000000-0000-0000-0001-000000000003"),
                        Name = "Plan Oro", SpeedMb = 80, MonthlyPrice = 199.00m,
                        IsActive = true, CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                    }
                );
            }

            await db.SaveChangesAsync();
            Log.Information("Datos iniciales insertados. Usuario admin creado (ver .env o documentación para credenciales).");
        }

        // ── Seed del usuario bot del chatbot (idempotente) ────────────────────
        // Solo se crea si SISTEMA_BOT_PASSWORD está definida en el entorno.
        // El bot usa rol Tecnico para acceder a GET /api/clients/{id}/invoices
        // y POST /api/tickets (política AdminOrTecnico).
        var botPassword = builder.Configuration["SISTEMA_BOT_PASSWORD"]
            ?? Environment.GetEnvironmentVariable("SISTEMA_BOT_PASSWORD");
        var botEmail    = builder.Configuration["SISTEMA_BOT_EMAIL"]
            ?? Environment.GetEnvironmentVariable("SISTEMA_BOT_EMAIL")
            ?? "bot@telecombolivianet.bo";

        if (!string.IsNullOrWhiteSpace(botPassword))
        {
            var botExists = await db.UserSystems.AnyAsync(u => u.Email == botEmail);
            if (!botExists)
            {
                var hasher   = scope.ServiceProvider.GetRequiredService<TelecomBoliviaNet.Application.Interfaces.IPasswordHasher>();
                var botHash  = hasher.Hash(botPassword);
                db.UserSystems.Add(new TelecomBoliviaNet.Domain.Entities.Auth.UserSystem
                {
                    FullName               = "Bot Chatbot WhatsApp",
                    Email                  = botEmail,
                    PasswordHash           = botHash,
                    Role                   = TelecomBoliviaNet.Domain.Entities.Auth.UserRole.Tecnico,
                    Status                 = TelecomBoliviaNet.Domain.Entities.Auth.UserStatus.Activo,
                    RequiresPasswordChange = false,
                    FailedLoginAttempts    = 0,
                    CreatedAt              = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
                Log.Information("Usuario bot creado: {Email} (rol Tecnico)", botEmail);
            }
            else
            {
                Log.Information("Usuario bot ya existe: {Email}", botEmail);
            }
        }
    }

    // ── Pipeline HTTP ─────────────────────────────────────────────────────────
    app.UseSerilogRequestLogging();
    app.UseStaticFiles();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "TelecomBoliviaNet API v1");
        c.RoutePrefix = "swagger";
    });

    app.UseExceptionHandler();     // FIX-20: GlobalExceptionHandler antes del pipeline de auth

    // FIX-31: headers de seguridad HTTP en todas las respuestas
    app.Use(async (ctx, next) =>
    {
        ctx.Response.Headers["X-Frame-Options"]           = "DENY";
        ctx.Response.Headers["X-Content-Type-Options"]    = "nosniff";
        ctx.Response.Headers["Referrer-Policy"]           = "strict-origin-when-cross-origin";
        ctx.Response.Headers["X-XSS-Protection"]          = "0";  // moderno: confiar en CSP
        ctx.Response.Headers["Content-Security-Policy"]   =
            "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:";
        await next();
    });

    app.UseCors("Frontend");
    app.UseRateLimiter();          // Rate limiting antes de autenticación
    app.UseAuthentication();
    app.UseMiddleware<TokenBlacklistMiddleware>();
    app.UseAuthorization();
    app.MapControllers();

    // ── SignalR Hub ───────────────────────────────────────────────────────────
    // El frontend se conecta a: ws://host:5000/hubs/admin
    app.MapHub<AdminHub>("/hubs/admin").RequireCors("Frontend");

    app.MapGet("/", () => Results.Redirect("/swagger")).AllowAnonymous();

    // ── Health check ─────────────────────────────────────────────────────────
    // Requerido por el healthcheck de docker-compose:
    //   test: ["CMD", "curl", "-f", "http://localhost:5000/health"]
    app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

    Log.Information("API lista · Swagger: http://localhost:5000/swagger");
    Log.Information("SignalR Hub: ws://localhost:5000/hubs/admin");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "La aplicación terminó inesperadamente.");
}
finally
{
    await Log.CloseAndFlushAsync();
}