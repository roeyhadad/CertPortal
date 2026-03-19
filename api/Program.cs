// CertPortal - ASP.NET Core Minimal API
// .NET 8  |  SQL Server via Dapper
// =====================================================
// NuGet packages required:
//   dotnet add package Dapper
//   dotnet add package Microsoft.Data.SqlClient
// =====================================================

using Microsoft.Data.SqlClient;
using Dapper;
using System.Data;

var builder = WebApplication.CreateBuilder(args);

// ── CORS (allow same-origin from IIS, or adjust for your domain)
builder.Services.AddCors(options =>
{
    options.AddPolicy("CertPortal", policy =>
        policy.WithOrigins(
                builder.Configuration["AllowedOrigins"]?.Split(',') ?? ["*"])
              .AllowAnyMethod()
              .AllowAnyHeader());
});

// ── Connection factory (registered as scoped via factory)
builder.Services.AddScoped<IDbConnection>(_ =>
    new SqlConnection(builder.Configuration.GetConnectionString("Default")));

var app = builder.Build();
app.UseCors("CertPortal");
app.UseDefaultFiles();
app.UseStaticFiles();   // serves index.html from wwwroot

// ═══════════════════════════════════════════════════
//  CERTIFICATES
// ═══════════════════════════════════════════════════

// GET all (with optional filters)
app.MapGet("/api/certificates", async (
    IDbConnection db,
    int? teamId, int? typeId, string? status, string? search) =>
{
    var sql = @"
        SELECT c.Id, c.Name, c.Location, c.StartDate, c.ExpiryDate, c.Notes,
               c.CreatedAt, c.UpdatedAt, c.CreatedBy,
               t.Id AS TeamId,   t.Name AS TeamName,
               ct.Id AS TypeId,  ct.Name AS TypeName,
               DATEDIFF(DAY, GETDATE(), c.ExpiryDate) AS DaysLeft
        FROM   Certificates c
        JOIN   Teams             t  ON c.TeamId = t.Id
        JOIN   CertificateTypes  ct ON c.TypeId = ct.Id
        WHERE  1=1
          AND  (@TeamId IS NULL OR c.TeamId = @TeamId)
          AND  (@TypeId IS NULL OR c.TypeId = @TypeId)
          AND  (@Search IS NULL OR
                c.Name     LIKE '%' + @Search + '%' OR
                c.Location LIKE '%' + @Search + '%' OR
                c.Notes    LIKE '%' + @Search + '%')
        ORDER  BY c.ExpiryDate ASC";

    var rows = await db.QueryAsync<CertificateRow>(sql,
        new { TeamId = teamId, TypeId = typeId, Search = search });

    // Status filter in memory (avoids complex SQL CASE)
    if (!string.IsNullOrEmpty(status))
        rows = rows.Where(r => GetStatus(r.DaysLeft) == status);

    return Results.Ok(rows);
});

// GET single
app.MapGet("/api/certificates/{id:int}", async (int id, IDbConnection db) =>
{
    var row = await db.QueryFirstOrDefaultAsync<CertificateRow>(
        @"SELECT c.Id, c.Name, c.Location, c.StartDate, c.ExpiryDate, c.Notes,
                 c.CreatedAt, c.UpdatedAt, c.CreatedBy,
                 t.Id AS TeamId, t.Name AS TeamName,
                 ct.Id AS TypeId, ct.Name AS TypeName,
                 DATEDIFF(DAY, GETDATE(), c.ExpiryDate) AS DaysLeft
          FROM   Certificates c
          JOIN   Teams t            ON c.TeamId = t.Id
          JOIN   CertificateTypes ct ON c.TypeId = ct.Id
          WHERE  c.Id = @Id", new { Id = id });

    return row is null ? Results.NotFound() : Results.Ok(row);
});

// POST create
app.MapPost("/api/certificates", async (CreateCertRequest req, IDbConnection db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name) || req.TeamId == 0 ||
        req.TypeId == 0 || string.IsNullOrWhiteSpace(req.Location) ||
        req.ExpiryDate == default)
        return Results.BadRequest("שדות חובה חסרים");

    var id = await db.QuerySingleAsync<int>(@"
        INSERT INTO Certificates (Name, TeamId, TypeId, Location, StartDate, ExpiryDate, Notes, CreatedBy)
        VALUES (@Name, @TeamId, @TypeId, @Location, @StartDate, @ExpiryDate, @Notes, @CreatedBy);
        SELECT SCOPE_IDENTITY();", req);

    return Results.Created($"/api/certificates/{id}", new { id });
});

// PUT update
app.MapPut("/api/certificates/{id:int}", async (int id, CreateCertRequest req, IDbConnection db) =>
{
    var affected = await db.ExecuteAsync(@"
        UPDATE Certificates
        SET    Name       = @Name,
               TeamId     = @TeamId,
               TypeId     = @TypeId,
               Location   = @Location,
               StartDate  = @StartDate,
               ExpiryDate = @ExpiryDate,
               Notes      = @Notes,
               UpdatedAt  = GETUTCDATE()
        WHERE  Id = @Id", new { req.Name, req.TeamId, req.TypeId, req.Location,
                                req.StartDate, req.ExpiryDate, req.Notes, Id = id });

    return affected == 0 ? Results.NotFound() : Results.NoContent();
});

// DELETE
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

app.MapGet("/api/teams", async (IDbConnection db) =>
    Results.Ok(await db.QueryAsync("SELECT Id, Name FROM Teams ORDER BY Name")));

app.MapPost("/api/teams", async (NameRequest req, IDbConnection db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("שם חסר");
    var id = await db.QuerySingleAsync<int>(
        "INSERT INTO Teams(Name) VALUES(@Name); SELECT SCOPE_IDENTITY();", req);
    return Results.Created($"/api/teams/{id}", new { id, req.Name });
});

app.MapGet("/api/certificatetypes", async (IDbConnection db) =>
    Results.Ok(await db.QueryAsync("SELECT Id, Name FROM CertificateTypes ORDER BY Name")));

app.MapPost("/api/certificatetypes", async (NameRequest req, IDbConnection db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest("שם חסר");
    var id = await db.QuerySingleAsync<int>(
        "INSERT INTO CertificateTypes(Name) VALUES(@Name); SELECT SCOPE_IDENTITY();", req);
    return Results.Created($"/api/certificatetypes/{id}", new { id, req.Name });
});

// ── Stats summary
app.MapGet("/api/stats", async (IDbConnection db) =>
{
    var rows = await db.QueryAsync<int>(@"
        SELECT DATEDIFF(DAY, GETDATE(), ExpiryDate) FROM Certificates");
    var days = rows.ToList();
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
//  HELPERS & MODELS
// ═══════════════════════════════════════════════════

static string GetStatus(int daysLeft) => daysLeft switch
{
    <= 0  => "expired",
    <= 7  => "urgent",
    <= 30 => "warn",
    _     => "ok"
};

record CertificateRow(
    int Id, string Name, string Location,
    DateTime? StartDate, DateTime ExpiryDate, string? Notes,
    DateTime CreatedAt, DateTime UpdatedAt, string? CreatedBy,
    int TeamId, string TeamName,
    int TypeId, string TypeName,
    int DaysLeft);

record CreateCertRequest(
    string Name, int TeamId, int TypeId,
    string Location, DateTime? StartDate,
    DateTime ExpiryDate, string? Notes, string? CreatedBy);

record NameRequest(string Name);
