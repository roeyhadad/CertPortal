// CertPortal v2 — ASP.NET Core Minimal API
// מבנה: Services → Certificates (היררכי)

using Microsoft.Data.SqlClient;
using Dapper;
using System.Data;

var builder = WebApplication.CreateBuilder(args);

// JSON — camelCase כדי שיתאים ל-JavaScript
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

builder.Services.AddCors(o => o.AddPolicy("cp",
    p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));
builder.Services.AddScoped<IDbConnection>(_ =>
    new SqlConnection(builder.Configuration.GetConnectionString("Default")));

var app = builder.Build();
app.UseCors("cp");
app.UseDefaultFiles();
app.UseStaticFiles();

// ═══════════════════════════════════════════════════
//  SERVICES
// ═══════════════════════════════════════════════════

app.MapGet("/api/services", async (IDbConnection db, int? teamId, string? search) =>
{
    var sql = @"
        SELECT s.Id, s.Name AS ServiceName, s.Description,
               t.Id AS TeamId, t.Name AS TeamName,
               c.Id              AS NextCertId,
               c.CommonName      AS NextCertName,
               c.CertificateType AS NextCertType,
               c.ExpiryDate      AS NextCertExpiry,
               DATEDIFF(DAY, GETDATE(), c.ExpiryDate) AS NextCertDaysLeft,
               (SELECT COUNT(*) FROM Certificates WHERE ServiceId = s.Id) AS CertCount
        FROM   Services s
        JOIN   Teams t ON s.TeamId = t.Id
        LEFT JOIN Certificates c ON c.Id = (
            SELECT TOP 1 Id FROM Certificates
            WHERE ServiceId = s.Id
            ORDER BY ExpiryDate ASC
        )
        WHERE  (@TeamId IS NULL OR s.TeamId = @TeamId)
          AND  (@Search IS NULL OR s.Name LIKE '%'+@Search+'%'
                               OR t.Name LIKE '%'+@Search+'%')
        ORDER BY c.ExpiryDate ASC";

    var rows = await db.QueryAsync<ServiceRow>(sql, new { TeamId = teamId, Search = search });
    return Results.Ok(rows);
});

app.MapGet("/api/services/{id:int}", async (int id, IDbConnection db) =>
{
    var svc = await db.QueryFirstOrDefaultAsync<ServiceDetail>(@"
        SELECT s.Id, s.Name AS ServiceName, s.Description,
               t.Id AS TeamId, t.Name AS TeamName
        FROM   Services s JOIN Teams t ON s.TeamId = t.Id
        WHERE  s.Id = @Id", new { Id = id });
    if (svc is null) return Results.NotFound();

    var certs = await db.QueryAsync<CertRow>(@"
        SELECT Id, CommonName, CertificateType, Issuer,
               IssuedDate, ExpiryDate,
               InstalledServers, InstalledLocation,
               RenewalContact, HowToVerify,
               ReferenceDesc, SpecialSettings, Contacts, Notes,
               CreatedAt, UpdatedAt,
               DATEDIFF(DAY, GETDATE(), ExpiryDate) AS DaysLeft
        FROM   Certificates
        WHERE  ServiceId = @Id
        ORDER  BY ExpiryDate ASC", new { Id = id });

    return Results.Ok(new { Service = svc, Certificates = certs });
});

app.MapPost("/api/services", async (ServiceRequest req, IDbConnection db) =>
{
    if (string.IsNullOrWhiteSpace(req.ServiceName) || req.TeamId == 0)
        return Results.BadRequest("שדות חובה חסרים");
    var id = await db.QuerySingleAsync<int>(@"
        INSERT INTO Services(Name,TeamId,Description,CreatedAt,UpdatedAt)
        VALUES(@ServiceName,@TeamId,@Description,GETUTCDATE(),GETUTCDATE());
        SELECT SCOPE_IDENTITY();", req);
    return Results.Created($"/api/services/{id}", new { id });
});

app.MapPut("/api/services/{id:int}", async (int id, ServiceRequest req, IDbConnection db) =>
{
    var n = await db.ExecuteAsync(@"
        UPDATE Services SET Name=@ServiceName, TeamId=@TeamId,
               Description=@Description, UpdatedAt=GETUTCDATE()
        WHERE Id=@Id", new { req.ServiceName, req.TeamId, req.Description, Id = id });
    return n == 0 ? Results.NotFound() : Results.NoContent();
});

app.MapDelete("/api/services/{id:int}", async (int id, IDbConnection db) =>
{
    await db.ExecuteAsync("DELETE FROM Certificates WHERE ServiceId=@Id", new { Id = id });
    var n = await db.ExecuteAsync("DELETE FROM Services WHERE Id=@Id", new { Id = id });
    return n == 0 ? Results.NotFound() : Results.NoContent();
});

// ═══════════════════════════════════════════════════
//  CERTIFICATES
// ═══════════════════════════════════════════════════

app.MapPost("/api/certificates", async (CertRequest req, IDbConnection db) =>
{
    if (string.IsNullOrWhiteSpace(req.CommonName) || req.ServiceId == 0 || req.ExpiryDate == default)
        return Results.BadRequest("שדות חובה חסרים");
    var id = await db.QuerySingleAsync<int>(@"
        INSERT INTO Certificates
            (ServiceId,CommonName,CertificateType,Issuer,IssuedDate,ExpiryDate,
             InstalledServers,InstalledLocation,RenewalContact,HowToVerify,
             ReferenceDesc,SpecialSettings,Contacts,Notes,CreatedAt,UpdatedAt)
        VALUES
            (@ServiceId,@CommonName,@CertificateType,@Issuer,@IssuedDate,@ExpiryDate,
             @InstalledServers,@InstalledLocation,@RenewalContact,@HowToVerify,
             @ReferenceDesc,@SpecialSettings,@Contacts,@Notes,GETUTCDATE(),GETUTCDATE());
        SELECT SCOPE_IDENTITY();", req);
    return Results.Created($"/api/certificates/{id}", new { id });
});

app.MapGet("/api/certificates/{id:int}", async (int id, IDbConnection db) =>
{
    var row = await db.QueryFirstOrDefaultAsync<CertRow>(@"
        SELECT Id,CommonName,CertificateType,Issuer,IssuedDate,ExpiryDate,
               InstalledServers,InstalledLocation,RenewalContact,HowToVerify,
               ReferenceDesc,SpecialSettings,Contacts,Notes,
               CreatedAt, UpdatedAt,
               DATEDIFF(DAY,GETDATE(),ExpiryDate) AS DaysLeft
        FROM   Certificates WHERE Id=@Id", new { Id = id });
    return row is null ? Results.NotFound() : Results.Ok(row);
});

app.MapPut("/api/certificates/{id:int}", async (int id, CertRequest req, IDbConnection db) =>
{
    var n = await db.ExecuteAsync(@"
        UPDATE Certificates SET
            CommonName=@CommonName, CertificateType=@CertificateType, Issuer=@Issuer,
            IssuedDate=@IssuedDate, ExpiryDate=@ExpiryDate,
            InstalledServers=@InstalledServers, InstalledLocation=@InstalledLocation,
            RenewalContact=@RenewalContact, HowToVerify=@HowToVerify,
            ReferenceDesc=@ReferenceDesc, SpecialSettings=@SpecialSettings,
            Contacts=@Contacts, Notes=@Notes, UpdatedAt=GETUTCDATE()
        WHERE Id=@Id",
        new { req.CommonName, req.CertificateType, req.Issuer, req.IssuedDate, req.ExpiryDate,
              req.InstalledServers, req.InstalledLocation, req.RenewalContact, req.HowToVerify,
              req.ReferenceDesc, req.SpecialSettings, req.Contacts, req.Notes, Id = id });
    return n == 0 ? Results.NotFound() : Results.NoContent();
});

app.MapDelete("/api/certificates/{id:int}", async (int id, IDbConnection db) =>
{
    var n = await db.ExecuteAsync("DELETE FROM Certificates WHERE Id=@Id", new { Id = id });
    return n == 0 ? Results.NotFound() : Results.NoContent();
});

// ═══════════════════════════════════════════════════
//  EXPIRING SOON
// ═══════════════════════════════════════════════════

app.MapGet("/api/expiring", async (IDbConnection db, int days = 30) =>
{
    var sql = @"
        SELECT c.Id, c.CommonName, c.CertificateType, c.ExpiryDate,
               c.InstalledServers, c.InstalledLocation, c.RenewalContact, c.Contacts,
               s.Name AS ServiceName,
               t.Name AS TeamName,
               DATEDIFF(DAY, GETDATE(), c.ExpiryDate) AS DaysLeft
        FROM   Certificates c
        JOIN   Services s ON c.ServiceId = s.Id
        JOIN   Teams    t ON s.TeamId    = t.Id
        WHERE  DATEDIFF(DAY, GETDATE(), c.ExpiryDate) BETWEEN 0 AND @Days
        ORDER  BY c.ExpiryDate ASC";

    var rows = await db.QueryAsync(sql, new { Days = days });
    return Results.Ok(rows);
});

// גם פגות תוקף (שליליות)
app.MapGet("/api/expired", async (IDbConnection db) =>
{
    var sql = @"
        SELECT c.Id, c.CommonName, c.CertificateType, c.ExpiryDate,
               c.InstalledServers, c.RenewalContact, c.Contacts,
               s.Name AS ServiceName,
               t.Name AS TeamName,
               DATEDIFF(DAY, GETDATE(), c.ExpiryDate) AS DaysLeft
        FROM   Certificates c
        JOIN   Services s ON c.ServiceId = s.Id
        JOIN   Teams    t ON s.TeamId    = t.Id
        WHERE  c.ExpiryDate < GETDATE()
        ORDER  BY c.ExpiryDate ASC";

    var rows = await db.QueryAsync(sql);
    return Results.Ok(rows);
});

// ═══════════════════════════════════════════════════
//  REFERENCE DATA
// ═══════════════════════════════════════════════════

app.MapGet("/api/teams", async (IDbConnection db) =>
    Results.Ok(await db.QueryAsync("SELECT Id, Name FROM Teams ORDER BY Name")));

app.MapPost("/api/teams", async (NameReq req, IDbConnection db) =>
{
    if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest();
    // אם קיים — החזר אותו
    var existing = await db.QueryFirstOrDefaultAsync<dynamic>(
        "SELECT Id, Name FROM Teams WHERE Name = @Name", req);
    if (existing != null)
        return Results.Ok(new { id = (int)existing.Id, name = (string)existing.Name });
    var id = await db.QuerySingleAsync<int>(
        "INSERT INTO Teams(Name) VALUES(@Name); SELECT SCOPE_IDENTITY();", req);
    return Results.Created($"/api/teams/{id}", new { id, name = req.Name });
});

app.MapGet("/api/stats", async (IDbConnection db) =>
{
    var days = (await db.QueryAsync<int>(
        "SELECT DATEDIFF(DAY,GETDATE(),ExpiryDate) FROM Certificates")).ToList();
    var svcCount = await db.QuerySingleAsync<int>("SELECT COUNT(*) FROM Services");
    return Results.Ok(new {
        Services = svcCount,
        Total    = days.Count,
        Ok       = days.Count(d => d > 30),
        Warning  = days.Count(d => d is > 0 and <= 30),
        Expired  = days.Count(d => d <= 0)
    });
});

app.Run();

// ═══════════════════════════════════════════════════
//  MODELS
// ═══════════════════════════════════════════════════

record ServiceRow(int Id, string ServiceName, string? Description,
    int TeamId, string TeamName,
    int? NextCertId, string? NextCertName, string? NextCertType,
    DateTime? NextCertExpiry, int? NextCertDaysLeft, int CertCount);

record ServiceDetail(int Id, string ServiceName, string? Description,
    int TeamId, string TeamName);

record CertRow(int Id, string CommonName, string CertificateType, string? Issuer,
    DateTime? IssuedDate, DateTime ExpiryDate,
    string? InstalledServers, string? InstalledLocation,
    string? RenewalContact, string? HowToVerify,
    string? ReferenceDesc, string? SpecialSettings,
    string? Contacts, string? Notes,
    DateTime? CreatedAt, DateTime? UpdatedAt, int DaysLeft);

record ServiceRequest(string ServiceName, int TeamId, string? Description);

record CertRequest(int ServiceId, string CommonName, string CertificateType,
    string? Issuer, DateTime? IssuedDate, DateTime ExpiryDate,
    string? InstalledServers, string? InstalledLocation,
    string? RenewalContact, string? HowToVerify,
    string? ReferenceDesc, string? SpecialSettings,
    string? Contacts, string? Notes);

record NameReq(string Name);
