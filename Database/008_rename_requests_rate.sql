-- Target database: FreightEstimator (never UpsRate).
--
-- Renames dbo.Requests_Rate to dbo.UpsRateRequestLog (the log of UPS rate calls: address, request, response) and its primary key,
-- recreates dbo.RecordRateRequestPlants and dbo.RecordRateRequestMatchKeys against the new name, and replaces dbo.LogRequest_Rate
-- with dbo.LogUpsRateRequest (same parameters and behaviour). Execute permissions on the old procedure are copied to the new one.
-- Rows and columns are unchanged. The UPSRate database is not touched: production UPSRate keeps Requests_Rate and LogRequest_Rate,
-- the Quote Log keeps reading it by that name, and Database/schema-fingerprint.sql will show the difference on purpose.
--
-- Safe to run more than once. Run it BEFORE re-running Database/FreightEstimator.sql on a database that still has the old table.
-- Deploy the app build that calls LogUpsRateRequest at the same time: until then the old build fails to find LogRequest_Rate.

IF DB_NAME() <> N'FreightEstimator'
BEGIN
    DECLARE @currentDb NVARCHAR(128) = DB_NAME();
    RAISERROR('Wrong database: this script must run in a database named FreightEstimator (current: %s). Nothing was changed.', 16, 1, @currentDb);
    SET NOEXEC ON;
END
GO

IF OBJECT_ID('dbo.Requests_Rate', 'U') IS NOT NULL AND OBJECT_ID('dbo.UpsRateRequestLog', 'U') IS NOT NULL
BEGIN
    RAISERROR('Both dbo.Requests_Rate and dbo.UpsRateRequestLog exist. Resolve by hand (compare rows, drop the empty one). Nothing was changed.', 16, 1);
    SET NOEXEC ON;
END
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID('dbo.Requests_Rate', 'U') IS NOT NULL
        EXEC sp_rename 'dbo.Requests_Rate', 'UpsRateRequestLog', 'OBJECT';

    IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'PK_Requests_Rate' AND parent_object_id = OBJECT_ID('dbo.UpsRateRequestLog'))
        EXEC sp_rename 'dbo.PK_Requests_Rate', 'PK_UpsRateRequestLog', 'OBJECT';

    -- These procedures name the table in their text, so recreate each from its own stored definition with the name swapped.
    DECLARE @def NVARCHAR(MAX);

    SET @def = OBJECT_DEFINITION(OBJECT_ID('dbo.RecordRateRequestPlants'));
    IF @def LIKE N'%Requests_Rate%'
    BEGIN
        SET @def = REPLACE(@def, N'Requests_Rate', N'UpsRateRequestLog');
        IF @def NOT LIKE N'%CREATE OR ALTER%' SET @def = STUFF(@def, CHARINDEX(N'CREATE', @def), LEN(N'CREATE'), N'CREATE OR ALTER');
        EXEC (@def);
    END

    SET @def = OBJECT_DEFINITION(OBJECT_ID('dbo.RecordRateRequestMatchKeys'));
    IF @def LIKE N'%Requests_Rate%'
    BEGIN
        SET @def = REPLACE(@def, N'Requests_Rate', N'UpsRateRequestLog');
        IF @def NOT LIKE N'%CREATE OR ALTER%' SET @def = STUFF(@def, CHARINDEX(N'CREATE', @def), LEN(N'CREATE'), N'CREATE OR ALTER');
        EXEC (@def);
    END

    -- LogRequest_Rate becomes LogUpsRateRequest: create the new one, copy who may run it, drop the old one.
    IF OBJECT_ID('dbo.LogRequest_Rate', 'P') IS NOT NULL
    BEGIN
        SET @def = OBJECT_DEFINITION(OBJECT_ID('dbo.LogRequest_Rate'));
        SET @def = REPLACE(REPLACE(@def, N'LogRequest_Rate', N'LogUpsRateRequest'), N'Requests_Rate', N'UpsRateRequestLog');
        IF @def NOT LIKE N'%CREATE OR ALTER%' SET @def = STUFF(@def, CHARINDEX(N'CREATE', @def), LEN(N'CREATE'), N'CREATE OR ALTER');
        EXEC (@def);

        DECLARE @grants NVARCHAR(MAX) = (
            SELECT STRING_AGG(CAST('GRANT ' + p.permission_name + ' ON OBJECT::dbo.LogUpsRateRequest TO ' + QUOTENAME(dp.name) AS NVARCHAR(MAX)), '; ')
            FROM sys.database_permissions p
            JOIN sys.database_principals dp ON dp.principal_id = p.grantee_principal_id
            WHERE p.major_id = OBJECT_ID('dbo.LogRequest_Rate') AND p.class = 1 AND p.state IN ('G', 'W'));
        IF @grants IS NOT NULL EXEC (@grants);

        DROP PROCEDURE dbo.LogRequest_Rate;
    END

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH
GO

SET NOEXEC OFF;
GO

-- Every row should say OK.
SELECT DB_NAME() AS [Database], c.Expected, CASE WHEN c.Ok = 1 THEN 'OK' ELSE 'PROBLEM' END AS Status
FROM (VALUES
    ('dbo.UpsRateRequestLog exists',                      CASE WHEN OBJECT_ID('dbo.UpsRateRequestLog', 'U') IS NOT NULL THEN 1 ELSE 0 END),
    ('dbo.Requests_Rate is gone',                         CASE WHEN OBJECT_ID('dbo.Requests_Rate', 'U') IS NULL THEN 1 ELSE 0 END),
    ('dbo.LogUpsRateRequest exists',                      CASE WHEN OBJECT_ID('dbo.LogUpsRateRequest', 'P') IS NOT NULL THEN 1 ELSE 0 END),
    ('dbo.LogRequest_Rate is gone',                       CASE WHEN OBJECT_ID('dbo.LogRequest_Rate', 'P') IS NULL THEN 1 ELSE 0 END),
    ('RecordRateRequestPlants reads the new name',        CASE WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.RecordRateRequestPlants')) LIKE N'%UpsRateRequestLog%'
                                                               AND OBJECT_DEFINITION(OBJECT_ID('dbo.RecordRateRequestPlants')) NOT LIKE N'%Requests_Rate%' THEN 1 ELSE 0 END),
    ('RecordRateRequestMatchKeys reads the new name',     CASE WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.RecordRateRequestMatchKeys')) LIKE N'%UpsRateRequestLog%'
                                                               AND OBJECT_DEFINITION(OBJECT_ID('dbo.RecordRateRequestMatchKeys')) NOT LIKE N'%Requests_Rate%' THEN 1 ELSE 0 END)
) c(Expected, Ok);
GO
