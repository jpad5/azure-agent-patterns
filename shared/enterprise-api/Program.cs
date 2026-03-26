using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/me", (HttpContext ctx) =>
{
    var claims = ctx.User.Claims.ToDictionary(c => c.Type, c => c.Value, StringComparer.OrdinalIgnoreCase);
    return Results.Ok(new
    {
        name = claims.GetValueOrDefault("name", "unknown"),
        email = claims.GetValueOrDefault("preferred_username", claims.GetValueOrDefault("email", "unknown")),
        oid = claims.GetValueOrDefault("http://schemas.microsoft.com/identity/claims/objectidentifier", "unknown"),
        scopes = claims.GetValueOrDefault("http://schemas.microsoft.com/identity/claims/scope", "unknown"),
        tokenSource = "OBO",
        timestamp = DateTime.UtcNow
    });
}).RequireAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

app.Run();
