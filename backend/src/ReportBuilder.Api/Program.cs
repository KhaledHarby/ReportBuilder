using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using ReportBuilder.Api;
using ReportBuilder.Api.Identity;
using ReportBuilder.Core.Identity;
using ReportBuilder.Infrastructure;
using ReportBuilder.Infrastructure.Data;
using ReportBuilder.Infrastructure.Identity;
using ReportBuilder.Infrastructure.Options;
using ReportBuilder.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

var connectionString = builder.Configuration.GetConnectionString("ReportBuilder")
    ?? throw new InvalidOperationException("Connection string 'ReportBuilder' is missing.");

var authOptions = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
if (string.IsNullOrWhiteSpace(authOptions.SigningKey) || authOptions.SigningKey.Length < 32)
{
    throw new InvalidOperationException("Auth:SigningKey must be at least 32 characters.");
}

builder.Services.AddReportBuilderInfrastructure(connectionString, builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = authOptions.Issuer,
            ValidAudience = authOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authOptions.SigningKey)),
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
        options.MapInboundClaims = true;
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var userIdValue = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var stampValue = context.Principal?.FindFirstValue(JwtTokenFactory.SecurityStampClaim);
                if (!Guid.TryParse(userIdValue, out var userId) || string.IsNullOrWhiteSpace(stampValue))
                {
                    context.Fail("Invalid session.");
                    return;
                }

                await using var scope = context.HttpContext.RequestServices.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ReportBuilderDbContext>();
                // A canceled analytics request must not fail this check. That 401
                // made the browser refresh and rotate the session out from under the page.
                var user = await db.Users.AsNoTracking()
                    .Where(item => item.Id == userId)
                    .Select(item => new { item.SecurityStamp, item.IsActive })
                    .FirstOrDefaultAsync();

                if (user is null
                    || !user.IsActive
                    || !string.Equals(user.SecurityStamp.ToString(), stampValue, StringComparison.OrdinalIgnoreCase))
                {
                    context.Fail("Session revoked.");
                }
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddHostedService<ActivityLogPurgeHostedService>();
builder.Services.AddHostedService<ReportScheduleHostedService>();
builder.Services.AddHostedService<DatasetCacheHostedService>();
if (AnalyticsSqlLogOptions.IsEnabled(builder.Configuration))
{
    builder.Services.AddHostedService<AnalyticsSqlLogWriter>();
}
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
    options.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
    options.Level = CompressionLevel.Fastest);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ReportExceptionHandler>();
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.JsonSerializerOptions.Converters.Add(new RuntimeObjectConverter());
    });
builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MECS Report Builder",
        Version = "v1",
        Description = "Design report templates and query live SQL Server tables and views."
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT access token. Paste the token only — Swagger adds the Bearer prefix.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document)] = []
    });
});
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.SetIsOriginAllowed(_ => true)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("Content-Disposition", ResponseServerHeaderMiddleware.HostNameHeader));
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

var migrateOnly = args.Contains("--migrate-only", StringComparer.OrdinalIgnoreCase);
if (migrateOnly)
{
    await app.Services.ApplyDatabaseMigrationsAsync();
    return;
}

await app.Services.InitializeDatabaseAsync();

app.UseForwardedHeaders();
app.UseResponseCompression();
app.UseMiddleware<ResponseServerHeaderMiddleware>();
app.Use((context, next) =>
{
    const string appPath = "/ReportBuilder";
    if (string.IsNullOrEmpty(context.Request.PathBase)
        && context.Request.Path.StartsWithSegments(appPath, out var rest))
    {
        context.Request.PathBase = appPath;
        context.Request.Path = rest;
    }

    return next();
});
app.UseCors();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseMiddleware<CurrentUserRolesMiddleware>();
app.UseAuthorization();
app.UseMiddleware<ActivityLoggingMiddleware>();

app.UseSwagger(options =>
{
    options.RouteTemplate = "swagger/{documentName}/swagger.json";
    options.PreSerializeFilters.Add((document, request) =>
    {
        var pathBase = request.PathBase.HasValue ? request.PathBase.Value : string.Empty;
        document.Servers =
        [
            new OpenApiServer
            {
                Url = $"{request.Scheme}://{request.Host.Value}{pathBase}"
            }
        ];
    });
});
app.UseSwaggerUI(options =>
{
    options.RoutePrefix = "swagger";
    options.SwaggerEndpoint("v1/swagger.json", "MECS Report Builder v1");
    options.DocumentTitle = "MECS Report Builder API";
    options.DisplayRequestDuration();
    options.ConfigObject.PersistAuthorization = true;
});

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.MapControllers();

var spaPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(spaPath))
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
    app.MapFallback(async context =>
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/api") || path.StartsWithSegments("/swagger"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(Path.Combine(spaPath, "index.html"));
    }).Add(builder =>
    {
        builder.Metadata.Add(new HttpMethodMetadata(["GET", "HEAD"]));
    });
}

app.Run();
