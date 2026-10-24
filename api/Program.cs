using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Wintech.Hrms.Api.Common.Auditing;
using Wintech.Hrms.Api.Common.Configuration;
using Wintech.Hrms.Api.Common.CurrentUser;
using Wintech.Hrms.Api.Common.ErrorHandling;
using Wintech.Hrms.Api.Common.Notifications;
using Wintech.Hrms.Api.Common.RateLimiting;
using Wintech.Hrms.Api.Common.Registration;
using Wintech.Hrms.Api.Common.Time;
using Wintech.Hrms.Api.Data;
using Wintech.Hrms.Api.Security.Auth;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// ---------------------------------------------------------------- configuration
var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var databaseOptions = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
var rateLimitOptions = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new RateLimitOptions();
var securityOptions = configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();
var corsOptions = configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();

if (jwtOptions.SigningKey.Length < 32 ||
    (!builder.Environment.IsDevelopment() && jwtOptions.SigningKey.StartsWith("DEV-ONLY", StringComparison.Ordinal)))
{
    throw new InvalidOperationException("Configure Jwt:SigningKey with a unique secret of at least 32 characters outside Development.");
}

builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton(databaseOptions);
builder.Services.AddSingleton(rateLimitOptions);
builder.Services.AddSingleton(securityOptions);
builder.Services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));
builder.Services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));

// ---------------------------------------------------------------- database
builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (databaseOptions.IsSqlServer)
        options.UseSqlServer(databaseOptions.ConnectionString,
            sql => sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null));
    else if (databaseOptions.IsPostgreSql)
        options.UseNpgsql(databaseOptions.ResolvePostgreSqlConnectionString(),
            postgres => postgres.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null));
    else if (databaseOptions.IsSqlite)
        options.UseSqlite(databaseOptions.ResolveSqliteConnectionString(builder.Environment.ContentRootPath));
    else
        options.UseInMemoryDatabase("wintech-hrms");
});

// ---------------------------------------------------------------- authentication (JWT, FR-EMP-001/005)
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey))
        };

        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                if (context.Response.HasStarted) return;
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new ApiError
                {
                    Status = 401,
                    Title = "Authentication required",
                    Detail = "A valid access token is required for this endpoint.",
                    CorrelationId = ApiErrorFactory.CorrelationId(context.HttpContext)
                });
            },
            OnForbidden = async context =>
            {
                if (context.Response.HasStarted) return;
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new ApiError
                {
                    Status = 403,
                    Title = "Access denied",
                    Detail = "You do not have permission to perform this action.",
                    CorrelationId = ApiErrorFactory.CorrelationId(context.HttpContext)
                });
            }
        };
    });

// ---------------------------------------------------------------- authorization (RBAC permissions)
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionHandler>();

// ---------------------------------------------------------------- MVC / API surface
builder.Services
    .AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        // Maps automatic model-validation failures onto the documented error envelope without
        // naming MVC types explicitly (kept here so Common/ stays framework-agnostic).
        options.InvalidModelStateResponseFactory = context =>
        {
            var validationErrors = new Dictionary<string, string[]>();
            foreach (var entry in context.ModelState)
            {
                if (entry.Value?.Errors is not { Count: > 0 }) continue;
                validationErrors[string.IsNullOrEmpty(entry.Key) ? "_" : entry.Key] = entry.Value.Errors
                    .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "The value is not valid." : e.ErrorMessage)
                    .ToArray();
            }

            var error = ApiErrorFactory.Validation(context.HttpContext, validationErrors);
            return new BadRequestObjectResult(error)
            {
                ContentTypes = { "application/json" },
                StatusCode = error.Status
            };
        };
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Wintech HRMS API",
        Version = "v1",
        Description = "Mobile-first HRMS & employee CRM API. See docs/API_CONTRACT.md for the versioned contract.",
        Contact = new OpenApiContact { Name = "Wintech Engineering" }
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the accessToken returned by /api/v1/auth/login"
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();

// ---------------------------------------------------------------- CORS (future admin console / web clients)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowedOrigins", policy =>
    {
        if (corsOptions.AllowedOrigins.Length > 0)
            policy.WithOrigins(corsOptions.AllowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        else
            policy.AllowAnyHeader().AllowAnyMethod();
    });
});

// ---------------------------------------------------------------- cross-cutting services
builder.Services.AddHrmsRateLimiter(rateLimitOptions);
builder.Services.AddScoped<IClock, SystemClock>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
// Fallback notification publisher; the Notifications module's implementation registers later and wins (last registration).
builder.Services.AddScoped<INotificationPublisher, NoopNotificationPublisher>();
// Auto-registers every IAppService implementation (auth, leave, cases, ...) and IDataSeeder — no edits needed per module.
builder.Services.AddAppServices(typeof(Program).Assembly);

// QuestPDF powers payslip/letter generation (server-side, no external service).
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var app = builder.Build();

// ---------------------------------------------------------------- pipeline
app.UseErrorEnvelopeStatusCodes();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowedOrigins");
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<AccountStatusMiddleware>(); // FR-EMP-006 / BR-008: inactive accounts are cut off immediately
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", async (AppDbContext db, CancellationToken cancellationToken) =>
{
    var databaseUp = await db.Database.CanConnectAsync(cancellationToken);
    return Results.Ok(new
    {
        status = databaseUp ? "Healthy" : "Degraded",
        database = databaseUp ? "Up" : "Down",
        time = DateTime.UtcNow,
        service = "wintech-hrms-api"
    });
}).AllowAnonymous().WithTags("Meta");

app.MapGet("/", () => Results.Ok(new
{
    service = "Wintech HRMS API",
    version = "v1",
    docs = "/swagger",
    health = "/health",
    contract = "docs/API_CONTRACT.md"
})).AllowAnonymous().WithTags("Meta");

// ---------------------------------------------------------------- initialise database + seed
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.Database.IsRelational() && (await db.Database.GetPendingMigrationsAsync()).Any())
            await db.Database.MigrateAsync();
        else
            await db.Database.EnsureCreatedAsync();

        if (databaseOptions.Seed)
        {
            var seeders = scope.ServiceProvider.GetServices<IDataSeeder>().OrderBy(s => s.Order).ThenBy(s => s.GetType().Name);
            foreach (var seeder in seeders)
            {
                await seeder.SeedAsync(CancellationToken.None);
            }
            logger.LogInformation("Database initialised ({Provider}) with {SeederCount} seeder pass(es).",
                databaseOptions.Provider, seeders.Count());
        }
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "Database initialisation failed.");
        if (databaseOptions.ExitOnInitializationFailure && !app.Environment.IsDevelopment()) throw;
    }
}

app.Run();

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program;
