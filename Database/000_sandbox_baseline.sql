-- Target database: SANDBOX UpsRate (the database MUST be named UpsRate: LogRequest_Rate and
-- LogResultsLTL write to [UpsRate].[dbo].[...] by three-part name, exactly as in production).
--
-- NEVER run this against production. Production already has all of these objects.
--
-- Baseline for the base objects the app needs that no other script in this folder creates.
-- Run this first, then 001 to 005 in order, then Database/schema-fingerprint.sql on both
-- servers and diff the outputs.
--
-- Provenance:
--   * The three stored procedures are copied from production (GetPlantCharges, LogRequest_Rate,
--     LogResultsLTL), with their formatting restored.
--   * Requests_Rate: columns and types from the production schema fingerprint. The primary key on
--     RequestId is assumed.
--   * Log_LTL and PiedmontAcctsForSpecialAlpRates: INFERRED from the columns the procedures use,
--     not read from production. Confirm with the fingerprint diff (both tables are in it) and
--     correct this file if production has more columns, other types, or an identity column.
--   * Plants.UpsShippingNumber is added if missing, because the plant queries filter on it.
--     The placeholder value is sandbox data only.
--
-- Requires SQL Server 2016 SP1+ (CREATE OR ALTER); 004 itself needs 2017+.

-- ---------------------------------------------------------------------------------------------
-- Guard: refuse to run anywhere but a database named UpsRate. SET NOEXEC ON skips every later
-- batch in this session until it is switched off again at the end.
-- ---------------------------------------------------------------------------------------------
IF DB_NAME() <> N'UpsRate'
BEGIN
    RAISERROR('Wrong database: this script must run in a database named UpsRate (current: %s). Nothing was changed.', 16, 1, N'see the Database column of the query window');
    SET NOEXEC ON;
END
GO

-- ---------------------------------------------------------------------------------------------
-- Plants: the column the plant queries need
-- ---------------------------------------------------------------------------------------------
IF COL_LENGTH('dbo.Plants', 'UpsShippingNumber') IS NULL
    ALTER TABLE dbo.Plants ADD UpsShippingNumber NCHAR(10) NULL;
GO

-- Production has Plants.GLCode INT NOT NULL (no code here reads it). Added with a throwaway default so
-- existing rows get a value, then the default is dropped to match production.
IF COL_LENGTH('dbo.Plants', 'GLCode') IS NULL
    ALTER TABLE dbo.Plants ADD GLCode INT NOT NULL CONSTRAINT DF_Plants_GLCode_tmp DEFAULT 0;
GO
IF EXISTS (SELECT 1 FROM sys.default_constraints WHERE name = 'DF_Plants_GLCode_tmp')
    ALTER TABLE dbo.Plants DROP CONSTRAINT DF_Plants_GLCode_tmp;
GO

-- Sandbox only: the app only checks that this is not NULL (the shipper-number lines in
-- UPSRequest are commented out), so any placeholder lets an active plant appear.
UPDATE dbo.Plants SET UpsShippingNumber = N'SANDBOX'
WHERE Active = 'Y' AND UpsShippingNumber IS NULL;
GO

-- ---------------------------------------------------------------------------------------------
-- Tables
-- ---------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.Requests_Rate', 'U') IS NULL
CREATE TABLE dbo.Requests_Rate
(
    RequestId    INT IDENTITY(1,1) NOT NULL,
    UserName     VARCHAR(50)       NULL,
    TargetUrl    VARCHAR(200)      NULL,
    Address      VARCHAR(200)      NULL,
    City         VARCHAR(200)      NULL,
    State        VARCHAR(50)       NULL,
    Zip          VARCHAR(50)       NULL,
    Country      VARCHAR(2)        NULL,
    RequestDate  DATETIME          NULL,
    RequestXml   NVARCHAR(MAX)     NULL,
    ResponseXml  NVARCHAR(MAX)     NULL,
    CONSTRAINT PK_Requests_Rate PRIMARY KEY (RequestId)
);
GO

-- Columns confirmed against the production fingerprint (the identity column was missing from the first draft).
IF OBJECT_ID('dbo.Log_LTL', 'U') IS NULL
CREATE TABLE dbo.Log_LTL
(
    LTLRequestId INT IDENTITY(1,1) NOT NULL,
    PlantCode    VARCHAR(10)   NULL,
    UserName     VARCHAR(50)   NULL,
    FullRequest  NVARCHAR(MAX) NULL,
    FullResults  NVARCHAR(MAX) NULL,
    XmlResponse  NVARCHAR(MAX) NULL,
    RequestDate  DATETIME      NULL
);
GO

-- INFERRED from GetPlantCharges (AccountNumber is compared to an INT parameter); confirm against production.
-- Accounts listed here get ALZ's charges returned under the plant code ALP.
IF OBJECT_ID('dbo.PiedmontAcctsForSpecialAlpRates', 'U') IS NULL
CREATE TABLE dbo.PiedmontAcctsForSpecialAlpRates
(
    AccountNumber INT NOT NULL
);
GO

-- ---------------------------------------------------------------------------------------------
-- Stored procedures (copied from production)
--
-- The comment blocks directly above each CREATE are part of the stored definition, so they are
-- reproduced exactly as in production; schema-fingerprint.sql hashes the text, comments included.
-- ---------------------------------------------------------------------------------------------
GO

-- =============================================
-- Author:		<Author,,Name>
-- Create date: <Create Date,,>
-- Description:	<Description,,>
-- =============================================
CREATE OR ALTER PROCEDURE [dbo].[GetPlantCharges]
    @Carrier VARCHAR(50),
    @AcctNumber INT
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS(SELECT AccountNumber FROM PiedmontAcctsForSpecialAlpRates WHERE AccountNumber = @AcctNumber)
        SELECT CASE WHEN PlantCode = 'ALZ' THEN 'ALP' ELSE PlantCode END as PlantCode,
               PerPackageCharge,
               PerShipmentCharge,
               NextDayAir,
               SecondDayAir,
               Ground,
               ThreeDaySelect,
               NextDayAirSaver,
               NextDayAirEarlyAM,
               SecondDayAirAM,
               Saver
        FROM PlantCarrierCharges2017
        WHERE CarrierId = @carrier
          AND PlantCode != 'ALP'
          AND Active = 'Y'
        ORDER BY
            CASE WHEN PlantCode = 'ALZ' THEN 'ALP' ELSE PlantCode END
    ELSE
        SELECT PlantCode,
               PerPackageCharge,
               PerShipmentCharge,
               NextDayAir,
               SecondDayAir,
               Ground,
               ThreeDaySelect,
               NextDayAirSaver,
               NextDayAirEarlyAM,
               SecondDayAirAM,
               Saver
        FROM PlantCarrierCharges2017
        WHERE CarrierId = @carrier
          AND PlantCode != 'ALZ'
          AND Active = 'Y'
        ORDER BY
            PlantCode
END
GO

-- =============================================
-- Author:		<Author,,Name>
-- Create date: ??
-- Modifications:  2024-06-27 - Colin Schroeder - Changed parms @RequestXml, @ResponseXml from ntext to nvarchar(max) as part of UPS API updates
-- Description:	<Description,,>
-- =============================================
CREATE OR ALTER PROCEDURE [dbo].[LogRequest_Rate]
    @UserName varchar(50),
    @TargetUrl varchar(200),
    @Address varchar(200),
    @City varchar(200),
    @State varchar(50),
    @Zip varchar(50),
    @Country varchar(2),
    @RequestXml nvarchar(max),
    @ResponseXml nvarchar(max),
    @RequestId int OUTPUT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO [UpsRate].[dbo].[Requests_Rate]
           ([UserName]
           ,[TargetUrl]
           ,[Address]
           ,[City]
           ,[State]
           ,[Zip]
           ,[Country]
           ,[RequestDate]
           ,[RequestXml]
           ,[ResponseXml])
    VALUES
           (@UserName
           ,@TargetUrl
           ,@Address
           ,@City
           ,@State
           ,@Zip
           ,@Country
           ,GetDate()
           ,@RequestXml
           ,@ResponseXml)

    SELECT @RequestId = SCOPE_IDENTITY()
END
GO

-- =============================================
-- Author:		<Author,,Name>
-- Create date: ??
-- Modifications:  2024-06-27 - Colin Schroeder - Changed parms @FullRequest, @FullResults, @XmlResponse from ntext to nvarchar(max) as part of UPS API updates
-- Description:	<Description,,>
-- =============================================
CREATE OR ALTER PROCEDURE [dbo].[LogResultsLTL]
    @PlantCode varchar(10),
    @UserName varchar(50),
    @FullRequest nvarchar(max),
    @FullResults nvarchar(max),
    @XmlResponse nvarchar(max)
AS
BEGIN
    -- SET NOCOUNT ON added to prevent extra result sets from
    -- interfering with SELECT statements.
    SET NOCOUNT ON;

    INSERT INTO [UpsRate].[dbo].[Log_LTL]
           ([PlantCode]
           ,[UserName]
           ,[FullRequest]
           ,[FullResults]
           ,[XmlResponse]
           ,[RequestDate])
    VALUES
           (@PlantCode
           ,@UserName
           ,@FullRequest
           ,@FullResults
           ,@XmlResponse
           ,GetDate())
END
GO

SET NOEXEC OFF;
GO

-- Confirm the objects now exist in the current database; every row should say OK.
SELECT DB_NAME() AS [Database], o.name AS Expected, CASE WHEN x.object_id IS NULL THEN 'MISSING' ELSE 'OK' END AS Status
FROM (VALUES ('Plants'), ('PlantCarrierCharges2017'), ('Requests_Rate'), ('Log_LTL'),
             ('PiedmontAcctsForSpecialAlpRates'), ('GetPlantCharges'), ('LogRequest_Rate'), ('LogResultsLTL')) o(name)
LEFT JOIN sys.objects x ON x.name = o.name AND x.schema_id = SCHEMA_ID('dbo');
GO
