# CertPortal — הוראות פריסה ב-IIS + SQL Server

## מבנה הפרויקט

```
CertPortal/
├── api/
│   ├── Program.cs          ← ASP.NET Core Minimal API
│   ├── CertPortal.csproj   ← NuGet dependencies
│   └── appsettings.json    ← Connection String
├── wwwroot/
│   └── index.html          ← ממשק המשתמש
├── sql/
│   └── 001_CreateSchema.sql← SQL Server schema + seed data
└── web.config              ← IIS configuration
```

---

## שלב 1 — הרצת SQL

פתח SSMS וחבר לשרת. הרץ את הקובץ:
```
sql/001_CreateSchema.sql
```
זה יצור:
- Database: `CertPortal`
- Tables: `Teams`, `CertificateTypes`, `Certificates`
- Seed data (נתוני דמו)

---

## שלב 2 — הגדרת Connection String

ערוך את `api/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "Default": "Server=YOUR_SERVER_NAME;Database=CertPortal;User Id=YOUR_USER;Password=YOUR_PASSWORD;TrustServerCertificate=True;Encrypt=True;"
  }
}
```

**דוגמאות:**
```json
// Windows Auth (מומלץ)
"Server=SQL01;Database=CertPortal;Integrated Security=True;TrustServerCertificate=True;"

// SQL Auth
"Server=SQL01\\SQLEXPRESS;Database=CertPortal;User Id=certportal_user;Password=StrongPass123!;TrustServerCertificate=True;"

// Named Instance
"Server=SQL01\\INSTANCE01;Database=CertPortal;Integrated Security=True;TrustServerCertificate=True;"
```

---

## שלב 3 — Build ו-Publish

```powershell
cd D:\Project\CertPortal\api

# שיטה 1: פרסום לתיקייה
dotnet publish -c Release -o D:\inetpub\wwwroot\CertPortal

# שיטה 2: קובץ zip
dotnet publish -c Release -o ./publish
```

לאחר הפרסום, העתק ידנית את:
- `wwwroot/index.html` → `D:\inetpub\wwwroot\CertPortal\wwwroot\`

---

## שלב 4 — הגדרת IIS

### 4.1 — התקן ASP.NET Core Hosting Bundle
הורד מ: https://dotnet.microsoft.com/download/dotnet/8.0
בחר: **"ASP.NET Core Runtime — Hosting Bundle"**

### 4.2 — צור Application Pool
```
Name:           CertPortal
.NET CLR:       No Managed Code
Managed Pipeline: Integrated
Identity:       ApplicationPoolIdentity (או חשבון דומיין)
```

### 4.3 — צור Website / Application
```
Site Name:      CertPortal
Physical Path:  D:\inetpub\wwwroot\CertPortal
App Pool:       CertPortal
Port:           80 (או 443 עם SSL)
```

### 4.4 — הרשאות SQL
אם משתמשים ב-Windows Auth, תן הרשאה ל-IIS AppPool:
```sql
USE CertPortal;
CREATE LOGIN [IIS APPPOOL\CertPortal] FROM WINDOWS;
CREATE USER  [IIS APPPOOL\CertPortal] FOR LOGIN [IIS APPPOOL\CertPortal];
ALTER ROLE db_datareader ADD MEMBER [IIS APPPOOL\CertPortal];
ALTER ROLE db_datawriter  ADD MEMBER [IIS APPPOOL\CertPortal];
```

---

## שלב 5 — בדיקה

פתח דפדפן:
```
http://YOUR_SERVER/api/certificates   → צריך להחזיר JSON
http://YOUR_SERVER/                   → ממשק המשתמש
```

---

## מבנה ה-API

| Method | Path                        | תיאור               |
|--------|-----------------------------|---------------------|
| GET    | /api/certificates           | כל התעודות (עם פילטרים) |
| GET    | /api/certificates/{id}      | תעודה בודדת         |
| POST   | /api/certificates           | יצירת תעודה חדשה    |
| PUT    | /api/certificates/{id}      | עדכון תעודה         |
| DELETE | /api/certificates/{id}      | מחיקת תעודה         |
| GET    | /api/teams                  | רשימת צוותים        |
| POST   | /api/teams                  | הוספת צוות          |
| GET    | /api/certificatetypes       | סוגי תעודות         |
| GET    | /api/stats                  | סטטיסטיקות          |

### Query Parameters for GET /api/certificates
- `teamId` — מסנן לפי צוות
- `typeId` — מסנן לפי סוג
- `status` — ok | warn | urgent | expired
- `search` — חיפוש חופשי

---

## פתרון בעיות

| שגיאה | סיבה | פתרון |
|-------|------|-------|
| 500 Internal Server | Connection String שגוי | בדוק appsettings.json |
| 503 Service Unavailable | AppPool קרס | בדוק Event Viewer |
| dotnet לא זמין | Hosting Bundle חסר | התקן .NET 8 Hosting Bundle |
| לא ניתן לחבר ל-SQL | Firewall / Auth | בדוק הרשאות ו-port 1433 |
