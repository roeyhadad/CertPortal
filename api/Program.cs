// CertPortal - ASP.NET Core Minimal API
// .NET 8  |  SQL Server via Dapper
// Schema: dbo.Certificates + dbo.Teams (existing)

using Microsoft.Data.SqlClient;
using Dapper;
using System.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("CertPortal", policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

builder.Services.AddScoped<IDbConnection>(_ =>
    new SqlConnection(builder.Configuration.GetConnectionString("Default")));

var app = builder.Build();
app.UseCors("CertPortal");
app.UseDefaultFiles();
app.UseStaticFiles();

// ═══════════════════════════════════════════════════
//  CERTIFICATES
// ═══════════════════════════════════════════════════

app.MapGet("/api/certificates", async (
    IDbConnection db,
    string? teamName, string? certType, string? status, string? search) =>
{
    var sql = @"
        SELECT Id,
               CommonName        AS Name,
               TeamName,
               AppName,
               CertificateType   AS TypeName,
               InstalledLocation AS Location,
               Issuer,
               IssuedDate        AS StartDate,
               ExpiryDate,
               Notes,
               CreatedAt,
               UpdatedAt,
               DATEDIFF(DAY, GETDATE(), ExpiryDate) AS DaysLeft
        FROM   Certificates
        WHERE  1=1
          AND  (@TeamName IS NULL OR TeamName = @TeamName)
          AND  (@CertType IS NULL OR CertificateType = @CertType)
          AND  (@Search   IS NULL OR
                CommonName        LIKE '%' + @Search + '%' OR
                InstalledLocation LIKE '%' + @Search + '%' OR
                Notes             LIKE '%' + @Search + '%' OR
                AppName           LIKE '%' + @Search + '%')
        ORDER  BY ExpiryDate ASC";

    var rows = await db.QueryAsync<CertificateRow>(sql,
        new { TeamName = teamName, CertType = certType, Search = search });

    if (!string.IsNullOrEmpty(status))
        rows = rows.Where(r => GetStatus(r.DaysLeft) == status);

    return Results.Ok(rows);
});

app.MapGet("/api/certificates/{id:int}", async (int id, IDbConnection db) =>
{
    var row = await db.QueryFirstOrDefaultAsync<CertificateRow>(@"
        SELECT Id,
               CommonName        AS Name,
               TeamName,
               AppName,
               CertificateType   AS TypeName,
               InstalledLocation AS Location,
               Issuer,
               IssuedDate        AS StartDate,
               ExpiryDate,
               Notes,
               CreatedAt,
               UpdatedAt,
               DATEDIFF(DAY, GETDATE(), ExpiryDate) AS DaysLeft
        FROM   Certificates
        WHERE  Id = @Id", new { Id = id });

    return row is null ? Results.NotFound() : Results.Ok(row);
});

app.MapPost("/api/certificates", async (CreateCertRequest req, IDbConnection db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.TeamName) ||
        string.IsNullOrWhiteSpace(req.TypeName) || string.IsNullOrWhiteSpace(req.Location) ||
        req.ExpiryDate == default)
        return Results.BadRequest("שדות חובה חסרים");

    var id = await db.QuerySingleAsync<int>(@"
        INSERT INTO Certificates
            (CommonName, TeamName, AppName, CertificateType, InstalledLocation,
             Issuer, IssuedDate, ExpiryDate, Notes, CreatedAt, UpdatedAt)
        VALUES
            (@Name, @TeamName, @AppName, @TypeName, @Location,
             @Issuer, @StartDate, @ExpiryDate, @Notes, GETUTCDATE(), GETUTCDATE());
        SELECT SCOPE_IDENTITY();", req);

    return Results.Created($"/api/certificates/{id}", new { id });
});

app.MapPut("/api/certificates/{id:int}", async (int id, CreateCertRequest req, IDbConnection db) =>
{
    var affected = await db.ExecuteAsync(@"
        UPDATE Certificates
        SET    CommonName        = @Name,
               TeamName          = @TeamName,
               AppName           = @AppName,
               CertificateType   = @TypeName,
               InstalledLocation = @Location,
               Issuer            = @Issuer,
               IssuedDate        = @StartDate,
               ExpiryDate        = @ExpiryDate,
               Notes             = @Notes,
               UpdatedAt         = GETUTCDATE()
        WHERE  Id = @Id",
        new { req.Name, req.TeamName, req.AppName, req.TypeName,
              req.Location, req.Issuer, req.StartDate, req.ExpiryDate, req.Notes, Id = id });

    return affected == 0 ? Results.NotFound() : Results.NoContent();
});

app.MapDelete("/api/certificates/{id:int}", async (int id, IDbConnection db) =>
{
    var affected = await db.ExecuteAsync(
        "DELETE FROM Certificates WHERE Id = @Id", new { Id = id });
    return affected == 0 ? Results.NotFound() : Results.NoContent();
});

// ═══════════════════════════════════════════════════
//  REFERENCE DATA
// ═══════════════════════════════════════════════════

app.MapGet("/api/teams", async (IDbConnection db) =>
    Results.Ok(await db.QueryAsync("SELECT Id, Name FROM Teams ORDER BY Name")));

app.MapPost("/api/teams", async (NameRequest req, IDbConnection db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("שם חסר");
    var id = await db.QuerySingleAsync<int>(
        "INSERT INTO Teams(Name, CreatedAt) VALUES(@Name, GETUTCDATE()); SELECT SCOPE_IDENTITY();", req);
    return Results.Created($"/api/teams/{id}", new { id, req.Name });
});

// סוגי תעודות — נלקח מהנתונים הקיימים
app.MapGet("/api/certificatetypes", async (IDbConnection db) =>
{
    var types = (await db.QueryAsync<string>(
        "SELECT DISTINCT CertificateType FROM Certificates WHERE CertificateType IS NOT NULL ORDER BY CertificateType"))
        .ToList();
    return Results.Ok(types.Select((t, i) => new { Id = i + 1, Name = t }));
});

app.MapGet("/api/stats", async (IDbConnection db) =>
{
    var days = (await db.QueryAsync<int>(
        "SELECT DATEDIFF(DAY, GETDATE(), ExpiryDate) FROM Certificates")).ToList();
    return Results.Ok(new
    {
        Total   = days.Count,
        Ok      = days.Count(d => d > 30),
        Warning = days.Count(d => d is > 0 and <= 30),
        Expired = days.Count(d => d <= 0)
    });
});

app.Run();

// ═══════════════════════════════════════════════════
//  MODELS
// ═══════════════════════════════════════════════════

static string GetStatus(int daysLeft) => daysLeft switch
{
    <= 0  => "expired",
    <= 7  => "urgent",
    <= 30 => "warn",
    _     => "ok"
};

record CertificateRow(
    int Id, string Name, string TeamName, string? AppName,
    string TypeName, string Location, string? Issuer,
    DateTime? StartDate, DateTime ExpiryDate, string? Notes,
    DateTime CreatedAt, DateTime UpdatedAt, int DaysLeft);

record CreateCertRequest(
    string Name, string TeamName, string? AppName,
    string TypeName, string Location, string? Issuer,
    DateTime? StartDate, DateTime ExpiryDate, string? Notes);

record NameRequest(string Name);
