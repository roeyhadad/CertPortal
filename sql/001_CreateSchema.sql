-- =====================================================
-- CertPortal - SQL Server Schema
-- =====================================================

USE master;
GO

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'CertPortal')
BEGIN
    CREATE DATABASE CertPortal;
END
GO

USE CertPortal;
GO

-- -----------------------------------------------------
-- Teams
-- -----------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Teams]') AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[Teams] (
        [Id]        INT IDENTITY(1,1) PRIMARY KEY,
        [Name]      NVARCHAR(100) NOT NULL,
        [CreatedAt] DATETIME2 DEFAULT GETUTCDATE(),
        CONSTRAINT UQ_Teams_Name UNIQUE ([Name])
    );
END
GO

-- -----------------------------------------------------
-- CertificateTypes
-- -----------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[CertificateTypes]') AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[CertificateTypes] (
        [Id]        INT IDENTITY(1,1) PRIMARY KEY,
        [Name]      NVARCHAR(100) NOT NULL,
        [CreatedAt] DATETIME2 DEFAULT GETUTCDATE(),
        CONSTRAINT UQ_CertTypes_Name UNIQUE ([Name])
    );
END
GO

-- -----------------------------------------------------
-- Certificates
-- -----------------------------------------------------
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[Certificates]') AND type = 'U')
BEGIN
    CREATE TABLE [dbo].[Certificates] (
        [Id]          INT IDENTITY(1,1) PRIMARY KEY,
        [Name]        NVARCHAR(255)   NOT NULL,
        [TeamId]      INT             NOT NULL,
        [TypeId]      INT             NOT NULL,
        [Location]    NVARCHAR(500)   NOT NULL,
        [StartDate]   DATE            NULL,
        [ExpiryDate]  DATE            NOT NULL,
        [Notes]       NVARCHAR(MAX)   NULL,
        [CreatedAt]   DATETIME2       DEFAULT GETUTCDATE(),
        [UpdatedAt]   DATETIME2       DEFAULT GETUTCDATE(),
        [CreatedBy]   NVARCHAR(100)   NULL,

        CONSTRAINT FK_Certs_Teams FOREIGN KEY ([TeamId]) REFERENCES [dbo].[Teams]([Id]),
        CONSTRAINT FK_Certs_Types FOREIGN KEY ([TypeId]) REFERENCES [dbo].[CertificateTypes]([Id])
    );
END
GO

-- Index for fast expiry queries
CREATE INDEX IF NOT EXISTS IX_Certificates_ExpiryDate ON [dbo].[Certificates]([ExpiryDate]);
CREATE INDEX IF NOT EXISTS IX_Certificates_TeamId     ON [dbo].[Certificates]([TeamId]);
CREATE INDEX IF NOT EXISTS IX_Certificates_TypeId     ON [dbo].[Certificates]([TypeId]);
GO

-- -----------------------------------------------------
-- Seed data
-- -----------------------------------------------------
IF NOT EXISTS (SELECT TOP 1 1 FROM [dbo].[Teams])
BEGIN
    INSERT INTO [dbo].[Teams] ([Name]) VALUES
        ('DevOps'), ('Backend'), ('Frontend'), ('Security'), ('Embedded');
END
GO

IF NOT EXISTS (SELECT TOP 1 1 FROM [dbo].[CertificateTypes])
BEGIN
    INSERT INTO [dbo].[CertificateTypes] ([Name]) VALUES
        ('SSL/TLS'), ('Code Signing'), ('Root CA'), ('S/MIME'),
        ('Client Auth'), ('Device Auth'), ('Intermediate CA');
END
GO

-- Sample certificates
IF NOT EXISTS (SELECT TOP 1 1 FROM [dbo].[Certificates])
BEGIN
    INSERT INTO [dbo].[Certificates] ([Name],[TeamId],[TypeId],[Location],[StartDate],[ExpiryDate],[Notes]) VALUES
    ('*.example.com Wildcard SSL',   1, 1, 'IIS Server 01 (Production)', DATEADD(DAY,-200,GETDATE()), DATEADD(DAY,3,GETDATE()),  N'חידוש דחוף! לפנות ל-GoDaddy'),
    ('Code Signing Certificate',     2, 2, 'Azure Key Vault',            DATEADD(DAY,-100,GETDATE()), DATEADD(DAY,15,GETDATE()), N'אחראי: ד. לוי'),
    ('Internal CA Root',             4, 3, 'PKI Server',                 DATEADD(DAY,-300,GETDATE()), DATEADD(DAY,45,GETDATE()), N'תעודת שורש פנימית'),
    ('api.example.com TLS',          2, 1, 'Nginx Prod Cluster',         DATEADD(DAY, -90,GETDATE()), DATEADD(DAY,90,GETDATE()), N'Let''s Encrypt - חידוש אוטומטי'),
    ('SMTP Email Certificate',       1, 4, 'Exchange Server',            DATEADD(DAY,-150,GETDATE()), DATEADD(DAY,180,GETDATE()),N''),
    ('Client Auth Certificate',      4, 5, 'VPN Gateway',               DATEADD(DAY, -50,GETDATE()), DATEADD(DAY,-5,GETDATE()),  N'פג תוקף! לחדש בדחיפות'),
    ('staging.example.com SSL',      3, 1, 'IIS Staging',               DATEADD(DAY, -30,GETDATE()), DATEADD(DAY,240,GETDATE()),N'סביבת staging'),
    ('IoT Device Certificate',       5, 6, 'Device Fleet',              DATEADD(DAY, -60,GETDATE()), DATEADD(DAY,8,GETDATE()),   N'120 מכשירים פעילים');
END
GO

PRINT 'Schema created successfully.';
GO
