-- Target database: FreightEstimator (never UpsRate).
--
-- Renames dbo.PlantCarrierCharges2017 to dbo.PlantCharges (the table holds the Plant Charges; the year and the carrier
-- were never part of what it is). Renames its two indexes and recreates dbo.GetPlantCharges against the new name.
-- Rows, columns and the procedure's signature are unchanged. The UPSRate database is not touched: production UPSRate
-- keeps the old name, so Database/schema-fingerprint.sql will show that difference on purpose.
--
-- Safe to run more than once. Run it BEFORE re-running Database/FreightEstimator.sql on a database that still has the
-- old table. Deploy the app build that reads dbo.PlantCharges at the same time: until then the old build fails to find
-- PlantCarrierCharges2017 (Plant.cs, the quote path).

IF DB_NAME() <> N'FreightEstimator'
BEGIN
    DECLARE @currentDb NVARCHAR(128) = DB_NAME();
    RAISERROR('Wrong database: this script must run in a database named FreightEstimator (current: %s). Nothing was changed.', 16, 1, @currentDb);
    SET NOEXEC ON;
END
GO

IF OBJECT_ID('dbo.PlantCarrierCharges2017', 'U') IS NOT NULL AND OBJECT_ID('dbo.PlantCharges', 'U') IS NOT NULL
BEGIN
    RAISERROR('Both dbo.PlantCarrierCharges2017 and dbo.PlantCharges exist. Resolve by hand (compare rows, drop the empty one). Nothing was changed.', 16, 1);
    SET NOEXEC ON;
END
GO

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID('dbo.PlantCarrierCharges2017', 'U') IS NOT NULL
        EXEC sp_rename 'dbo.PlantCarrierCharges2017', 'PlantCharges', 'OBJECT';

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PlantCarrierCharges2017_Id' AND object_id = OBJECT_ID('dbo.PlantCharges'))
        EXEC sp_rename 'dbo.PlantCharges.IX_PlantCarrierCharges2017_Id', 'IX_PlantCharges_Id', 'INDEX';

    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_PlantCarrierCharges2017_Plant' AND object_id = OBJECT_ID('dbo.PlantCharges'))
        EXEC sp_rename 'dbo.PlantCharges.IX_PlantCarrierCharges2017_Plant', 'IX_PlantCharges_Plant', 'INDEX';

    -- GetPlantCharges names the table in its text, so recreate it from its own stored definition with the name swapped.
    DECLARE @def NVARCHAR(MAX) = OBJECT_DEFINITION(OBJECT_ID('dbo.GetPlantCharges'));
    IF @def LIKE N'%PlantCarrierCharges2017%'
    BEGIN
        SET @def = REPLACE(@def, N'PlantCarrierCharges2017', N'PlantCharges');
        IF @def NOT LIKE N'%CREATE OR ALTER%'
            SET @def = STUFF(@def, CHARINDEX(N'CREATE', @def), LEN(N'CREATE'), N'CREATE OR ALTER');
        EXEC (@def);
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

-- Every row should say OK: the new table exists, the old one is gone, and the procedure no longer mentions the old name.
SELECT DB_NAME() AS [Database], c.Expected, CASE WHEN c.Ok = 1 THEN 'OK' ELSE 'PROBLEM' END AS Status
FROM (VALUES
    ('dbo.PlantCharges exists',                         CASE WHEN OBJECT_ID('dbo.PlantCharges', 'U') IS NOT NULL THEN 1 ELSE 0 END),
    ('dbo.PlantCarrierCharges2017 is gone',             CASE WHEN OBJECT_ID('dbo.PlantCarrierCharges2017', 'U') IS NULL THEN 1 ELSE 0 END),
    ('GetPlantCharges reads dbo.PlantCharges',          CASE WHEN OBJECT_DEFINITION(OBJECT_ID('dbo.GetPlantCharges')) LIKE N'%FROM PlantCharges%'
                                                             AND OBJECT_DEFINITION(OBJECT_ID('dbo.GetPlantCharges')) NOT LIKE N'%PlantCarrierCharges2017%' THEN 1 ELSE 0 END)
) c(Expected, Ok);
GO
