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

    // ── Validación de seguridad: JWT key por defecto detectada en producción ─────
    // La key por defecto está en el repositorio — un deploy sin .env correcto
    // permitiría forjar tokens válidos. Fail-fast es mejor que fallar en silencio.
    if (builder.Environment.IsProduction())
    {
        const string defaultKey = "TelecomBoliviaNet_JWT_SuperSecretKey_2025_MinLength32Chars!!";
        var jwtKey = builder.Configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey == defaultKey)
            throw new InvalidOperationException(
                "SEGURIDAD: Jwt:Key no está configurada o usa el valor por defecto inseguro. " +
                "Configura JWT_KEY en el archivo .env con un valor aleatorio de al menos 32 caracteres.");
    }

    // ── Todos los servicios de aplicación (DB, repos, servicios, validadores) ─
    builder.Services.AddApplicationServices(builder.Configuration);

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

    // ── Controllers + JSON ────────────────────────────────────────────────────
    builder.Services
        .AddControllers()
        .AddJsonOptions(o =>
        {
            o.JsonSerializerOptions.PropertyNamingPolicy = null; // PascalCase
        });

    // ── Swagger ───────────────────────────────────────────────────────────────
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwagger();

    var app = builder.Build();

    // ── Migraciones automáticas al iniciar ────────────────────────────────────
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Log.Information("Aplicando migraciones de base de datos...");

        var pending = await db.Database.GetPendingMigrationsAsync();
        var pendingList = pending.ToList();

        if (pendingList.Count > 0)
        {
            Log.Information("Migraciones pendientes: {Count}. Aplicando...", pendingList.Count);
            try
            {
                await db.Database.MigrateAsync();
                var applied = await db.Database.GetAppliedMigrationsAsync();
                Log.Information("Migraciones aplicadas correctamente: {Count} total.", applied.Count());
            }
            catch (Exception exMigrate)
            {
                Log.Fatal(exMigrate, "ERROR CRÍTICO en MigrateAsync — las tablas no fueron creadas. Revisa la cadena de conexión y permisos de la BD.");
                throw;
            }
        }
        else
        {
            var applied = await db.Database.GetAppliedMigrationsAsync();
            var appliedList = applied.ToList();
            if (appliedList.Count == 0)
            {
                Log.Warning("No se detectaron migraciones pendientes ni aplicadas. Ejecutando EnsureCreatedAsync como fallback...");
                await db.Database.EnsureCreatedAsync();
                Log.Information("Esquema de base de datos creado con EnsureCreatedAsync.");
            }
            else
            {
                Log.Information("Base de datos al día. Migraciones aplicadas: {Count}", appliedList.Count);
            }
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