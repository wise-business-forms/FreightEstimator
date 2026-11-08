-- =====================================================================================================
-- FreightEstimator database: DDL (structure only)
--
-- Replaces the SANDBOX UpsRate database for development and testing, on AZUREDB01\AZUREDB01.
--
-- NEVER run this against UPSRate, and never point the app or the tests at UPSRate. This script creates only
-- a database named FreightEstimator and refuses to create any object anywhere else. It contains no
-- three-part [UpsRate] names, so nothing here can reach the UPSRate database on the same server.
--
-- Who runs it: someone with CREATE ANY DATABASE (dbcreator or sysadmin) for part 1. If the database already
-- exists, part 1 does nothing and the rest needs only db_owner on it.
--
-- Safe to run more than once (every object is created only if missing; procedures use CREATE OR ALTER).
-- Needs SQL Server 2017+ (STRING_AGG, JSON); the target is SQL Server 2019.
--
-- Provenance of each part:
--   * Requests_Rate, Log_LTL, GetPlantCharges, LogRequest_Rate, LogResultsLTL, QuoteServiceCalculation,
--     CalculatedRateShadowLog, RateRequestPlant, RateRequestMatchKey, RateRequestServiceRate, Consumer, Quote:
--     the existing scripts Database/000 to 005, consolidated, with the three-part names removed.
--   * Plants and PlantCarrierCharges2017: column names and types read from UPSRate (schema only). Their keys
--     and indexes were not read. Rows for both come from Database/FreightEstimator_seed.sql and a one-off copy.
--   * FreightRequests: the CostPlus request/result layout, column names and types read from
--     UPSRate.dbo.FreightRequests_SANDBOX (schema only; nothing there was changed). Not read: its primary key
--     columns and the precision of the Rate columns.
--   * CostPlusRequestClaim, CostPlusReferenceNumber, CostPlusShadowResult (#36) and the "CostPlus" Consumer: new, for the CostPlus poller
--     (#31, ADR 0005).
--
-- Loads no data except the "CostPlus" Consumer row. Plants and PlantCarrierCharges2017 rows are loaded
-- separately (Database/FreightEstimator_seed.sql) before the app or the tests can quote.
-- =====================================================================================================

SET NOCOUNT ON;
GO

-- -----------------------------------------------------------------------------------------------------
-- Part 1: the database
-- -----------------------------------------------------------------------------------------------------
IF DB_ID(N'FreightEstimator') IS NULL
    CREATE DATABASE [FreightEstimator];
GO

-- Guard: from here on, run only inside a database named FreightEstimator. If the USE below failed (no such
-- database, or no access), SET NOEXEC ON skips every later batch so nothing is created in master or elsewhere.
USE [FreightEstimator];
GO
IF DB_NAME() <> N'FreightEstimator'
BEGIN
    RAISERROR('Wrong database: this script must run in a database named FreightEstimator. Nothing was changed.', 16, 1);
    SET NOEXEC ON;
END
GO

-- -----------------------------------------------------------------------------------------------------
-- Part 2: reference tables. Column names and types as read from UPSRate (schema only; nothing there was
-- changed). Not read: their keys and indexes, so the indexes below are this script's own choice.
-- -----------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.Plants', 'U') IS NULL
CREATE TABLE dbo.Plants
(
    PlantCode          CHAR(3)       NOT NULL,
    PlantName          VARCHAR(50)   NULL,
    Address            VARCHAR(100)  NULL,
    City               VARCHAR(100)  NULL,
    State              VARCHAR(50)   NULL,
    Zip                VARCHAR(50)   NULL,
    Country            CHAR(2)       NULL,
    Active             VARCHAR(1)    NULL,        -- 'Y' = quotable
    GLCode             INT           NOT NULL,    -- no code reads it
    UpsShippingNumber  NCHAR(10)     NULL,        -- the plant queries filter on NOT NULL
    INDEX IX_Plants_PlantCode (PlantCode)
);
GO

-- Markups are percentages (SandboxPlantCharges.Row). CarrierId is UPS, UPSCWT, GF or TI.
IF OBJECT_ID('dbo.PlantCarrierCharges2017', 'U') IS NULL
CREATE TABLE dbo.PlantCarrierCharges2017
(
    CarrierPlantChargesId  INT            NOT NULL,
    CarrierId              VARCHAR(50)    NOT NULL,
    PlantCode              CHAR(3)        NOT NULL,
    PerPackageCharge       MONEY          NULL,
    PerShipmentCharge      MONEY          NULL,
    NextDayAir             NUMERIC(18,4)  NULL,
    SecondDayAir           NUMERIC(18,4)  NULL,
    Ground                 NUMERIC(18,4)  NULL,
    ThreeDaySelect         NUMERIC(18,4)  NULL,
    NextDayAirSaver        NUMERIC(18,4)  NULL,
    NextDayAirEarlyAM      NUMERIC(18,4)  NULL,
    SecondDayAirAM         NUMERIC(18,4)  NULL,
    Saver                  NUMERIC(18,4)  NULL,
    Active                 VARCHAR(1)     NULL,
    -- No primary key: UPSRate holds rows that share a CarrierPlantChargesId (44 appears more than once).
    INDEX IX_PlantCarrierCharges2017_Id (CarrierPlantChargesId),
    INDEX IX_PlantCarrierCharges2017_Plant (PlantCode, CarrierId)
);
GO

-- -----------------------------------------------------------------------------------------------------
-- Part 3: logging, Quote API and reporting objects (Database/000 to 005, consolidated)
-- -----------------------------------------------------------------------------------------------------
GO

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

    INSERT INTO [dbo].[Requests_Rate]
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

    INSERT INTO [dbo].[Log_LTL]
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


-- Target database: FreightEstimator (same database as Requests_Rate / LogRequest_Rate / LogResultsLTL)
--
-- Purely additive: this script does not alter Requests_Rate, LogRequest_Rate, LogResultsLTL,
-- or any other existing table or stored procedure. It creates one new table and one new
-- stored procedure so the app can persist, per service returned in a Quote, the Calculated
-- Rate (the final marked-up total already shown on screen today) and the markup components
-- that produced it. Nothing currently reads or depends on these objects except the new
-- logging calls in Models/Services/Log.cs.
--
-- QuoteId is generated by the application (not a database identity) before either carrier's
-- existing logging call runs, so UPS and Logistics Partner (LTL) rows from the same quote
-- request share one key. See docs/adr/0002-app-generated-quote-id.md.

IF OBJECT_ID('dbo.QuoteServiceCalculation', 'U') IS NULL
CREATE TABLE dbo.QuoteServiceCalculation
(
    QuoteServiceCalculationId  INT IDENTITY(1,1)  NOT NULL,
    QuoteId                    UNIQUEIDENTIFIER    NOT NULL,
    Carrier                    VARCHAR(20)         NOT NULL,   -- 'UPS' or 'LogisticsPartner'
    PlantCode                  VARCHAR(10)         NULL,
    UserName                   VARCHAR(50)         NULL,
    ServiceCode                VARCHAR(20)         NULL,
    ServiceName                VARCHAR(100)        NULL,
    CarrierRate                DECIMAL(18,2)       NULL,       -- raw carrier rate used as the base (UPS published/negotiated, or LTL's raw partner total)
    IsNegotiatedRate           BIT                 NULL,       -- UPS only: whether the negotiated/CWT rate was used as the base
    PerPackageCharge           DECIMAL(18,2)       NULL,
    PerShipmentCharge          DECIMAL(18,2)       NULL,
    MarkupPercent              DECIMAL(9,4)        NULL,
    CalculatedRate             DECIMAL(18,2)       NOT NULL,   -- final marked-up total, matching what was shown on screen
    CreatedDate                DATETIME            NOT NULL    CONSTRAINT DF_QuoteServiceCalculation_CreatedDate DEFAULT (GETDATE()),
    CONSTRAINT PK_QuoteServiceCalculation PRIMARY KEY (QuoteServiceCalculationId)
);
GO

IF INDEXPROPERTY(OBJECT_ID('dbo.QuoteServiceCalculation'), 'IX_QuoteServiceCalculation_QuoteId', 'IndexID') IS NULL
CREATE INDEX IX_QuoteServiceCalculation_QuoteId ON dbo.QuoteServiceCalculation (QuoteId);
GO

CREATE OR ALTER PROCEDURE dbo.LogQuoteServiceCalculation
    @QuoteId            UNIQUEIDENTIFIER,
    @Carrier            VARCHAR(20),
    @PlantCode          VARCHAR(10)     = NULL,
    @UserName           VARCHAR(50)     = NULL,
    @ServiceCode        VARCHAR(20)     = NULL,
    @ServiceName        VARCHAR(100)    = NULL,
    @CarrierRate        DECIMAL(18,2)   = NULL,
    @IsNegotiatedRate   BIT             = NULL,
    @PerPackageCharge   DECIMAL(18,2)   = NULL,
    @PerShipmentCharge  DECIMAL(18,2)   = NULL,
    @MarkupPercent      DECIMAL(9,4)    = NULL,
    @CalculatedRate     DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.QuoteServiceCalculation
        (QuoteId, Carrier, PlantCode, UserName, ServiceCode, ServiceName,
         CarrierRate, IsNegotiatedRate, PerPackageCharge, PerShipmentCharge, MarkupPercent, CalculatedRate)
    VALUES
        (@QuoteId, @Carrier, @PlantCode, @UserName, @ServiceCode, @ServiceName,
         @CarrierRate, @IsNegotiatedRate, @PerPackageCharge, @PerShipmentCharge, @MarkupPercent, @CalculatedRate);
END
GO

-- Target database: FreightEstimator (same database as QuoteServiceCalculation, Requests_Rate, LogResultsLTL)
--
-- Purely additive, and purely diagnostic: this script does not alter any existing table or
-- stored procedure. It creates one new, throwaway table and one new stored procedure so the
-- Deepen Calculated Rate migration (docs/adr/0003-deepen-calculated-rate-via-shadow-mode.md)
-- can record, per quote/service/call-site, the value today's existing formula produced next to
-- the value the new CalculatedRate.Apply module produced, without changing what's persisted in
-- QuoteServiceCalculation or shown on screen. Intended to be dropped once all four call sites
-- listed in docs/issues/0001-ups-markup-divergence.md are migrated and live.

IF OBJECT_ID('dbo.CalculatedRateShadowLog', 'U') IS NULL
CREATE TABLE dbo.CalculatedRateShadowLog
(
    CalculatedRateShadowLogId  INT IDENTITY(1,1)  NOT NULL,
    QuoteId                    UNIQUEIDENTIFIER    NOT NULL,
    CallSite                   VARCHAR(30)         NOT NULL,   -- e.g. 'GRID2', 'GRID1Single', 'GRID1Multi', 'LTL'
    PlantCode                  VARCHAR(10)         NULL,
    ServiceCode                VARCHAR(20)         NULL,
    ServiceName                VARCHAR(100)        NULL,
    ExistingCalculatedRate     DECIMAL(18,2)       NOT NULL,   -- value from today's existing, still-live formula
    NewCalculatedRate          DECIMAL(18,2)       NOT NULL,   -- value from CalculatedRate.Apply (not shown to the requester yet)
    Delta                      DECIMAL(18,2)       NOT NULL,   -- NewCalculatedRate - ExistingCalculatedRate
    CreatedDate                DATETIME            NOT NULL    CONSTRAINT DF_CalculatedRateShadowLog_CreatedDate DEFAULT (GETDATE()),
    CONSTRAINT PK_CalculatedRateShadowLog PRIMARY KEY (CalculatedRateShadowLogId)
);
GO

IF INDEXPROPERTY(OBJECT_ID('dbo.CalculatedRateShadowLog'), 'IX_CalculatedRateShadowLog_CallSite', 'IndexID') IS NULL
CREATE INDEX IX_CalculatedRateShadowLog_CallSite ON dbo.CalculatedRateShadowLog (CallSite);
GO

CREATE OR ALTER PROCEDURE dbo.LogCalculatedRateShadowComparison
    @QuoteId                UNIQUEIDENTIFIER,
    @CallSite                VARCHAR(30),
    @PlantCode                VARCHAR(10)     = NULL,
    @ServiceCode            VARCHAR(20)     = NULL,
    @ServiceName            VARCHAR(100)    = NULL,
    @ExistingCalculatedRate DECIMAL(18,2),
    @NewCalculatedRate        DECIMAL(18,2)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.CalculatedRateShadowLog
        (QuoteId, CallSite, PlantCode, ServiceCode, ServiceName, ExistingCalculatedRate, NewCalculatedRate, Delta)
    VALUES
        (@QuoteId, @CallSite, @PlantCode, @ServiceCode, @ServiceName, @ExistingCalculatedRate, @NewCalculatedRate,
         @NewCalculatedRate - @ExistingCalculatedRate);
END
GO

-- Target database: FreightEstimator (same database as Requests_Rate and Plants)
--
-- Purely additive: this script does not alter Requests_Rate or any existing stored procedure.
-- Requests_Rate doesn't store which plant a quote shipped from; the Past Quotes report
-- (ReportsController.Quotes) works it out by matching the request JSON's ship-from city/state
-- against Plants. Doing that per row inside the report's query times out, so this table records
-- the match once per request and lets the report's plant filter use an indexed lookup instead.
--
-- Every request gets a row, including ones that match no plant (PlantCode NULL), so
-- RecordRateRequestPlants can catch up from MAX(RequestId) without re-reading old requests.
--
-- Running this script performs the one-time backfill at the end, which reads every row of
-- Requests_Rate and may take several minutes. After that, the report calls
-- RecordRateRequestPlants on each plant-filtered load, which only reads requests newer than the
-- last one recorded.

IF OBJECT_ID('dbo.RateRequestPlant', 'U') IS NULL
CREATE TABLE dbo.RateRequestPlant
(
    RequestId   INT          NOT NULL,
    PlantCode   VARCHAR(10)  NULL,        -- NULL: ship-from didn't match an active plant, or the request isn't JSON
    CONSTRAINT PK_RateRequestPlant PRIMARY KEY (RequestId)
);
GO

IF INDEXPROPERTY(OBJECT_ID('dbo.RateRequestPlant'), 'IX_RateRequestPlant_PlantCode', 'IndexID') IS NULL
CREATE INDEX IX_RateRequestPlant_PlantCode ON dbo.RateRequestPlant (PlantCode, RequestId);
GO

CREATE OR ALTER PROCEDURE dbo.RecordRateRequestPlants
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRANSACTION;

    -- Only one caller catches up at a time; a concurrent caller skips rather than waits,
    -- so two report loads can't insert the same RequestId.
    DECLARE @lock INT;
    EXEC @lock = sp_getapplock @Resource = 'RecordRateRequestPlants', @LockMode = 'Exclusive',
                               @LockOwner = 'Transaction', @LockTimeout = 0;
    IF @lock < 0
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @after INT = ISNULL((SELECT MAX(RequestId) FROM dbo.RateRequestPlant), 0);

    -- Same match the report does in C#: trimmed, case-insensitive ship-from city + state
    -- against active plants that have a UPS shipping number.
    INSERT INTO dbo.RateRequestPlant (RequestId, PlantCode)
    SELECT r.RequestId, p.PlantCode
    FROM dbo.Requests_Rate r
    CROSS APPLY (
        SELECT
            CASE WHEN ISJSON(r.RequestXml) = 1
                 THEN UPPER(LTRIM(RTRIM(JSON_VALUE(r.RequestXml, '$.RateRequest.Shipment.ShipFrom.Address.City')))) END AS City,
            CASE WHEN ISJSON(r.RequestXml) = 1
                 THEN UPPER(LTRIM(RTRIM(JSON_VALUE(r.RequestXml, '$.RateRequest.Shipment.ShipFrom.Address.StateProvinceCode')))) END AS State
    ) sf
    OUTER APPLY (
        SELECT TOP 1 pl.PlantCode
        FROM dbo.Plants pl
        WHERE pl.Active = 'Y'
          AND pl.UpsShippingNumber IS NOT NULL
          AND UPPER(LTRIM(RTRIM(pl.City))) = sf.City
          AND UPPER(LTRIM(RTRIM(pl.State))) = sf.State
        ORDER BY pl.PlantCode
    ) p
    WHERE r.RequestId > @after;

    COMMIT TRANSACTION;
END
GO

-- One-time backfill of existing requests.

-- Target database: FreightEstimator (same database as Requests_Rate, RateRequestPlant and QuoteServiceCalculation)
-- Requires: 003_create_rate_request_plant.sql. SQL Server 2017+ (STRING_AGG ... WITHIN GROUP).
--
-- Purely additive and purely derived: nothing here is a new fact. Both tables are re-derived from
-- Requests_Rate (and RateRequestPlant) and can be dropped and rebuilt at any time. They exist so the
-- Compare and Rate Changes reports (ReportsController, Models/Reports/RateChanges.cs) can find a
-- Quote's Match Group with an indexed lookup instead of parsing JSON per row, which times out.
--
-- RateRequestMatchKey: one row per request. The Match Group key is PlantCode + ShipToZip +
-- PackageCount + WeightsKey + ResidentialIndicatorSent (see CONTEXT.md, "Match Group"). WeightsKey is
-- each package weight rounded UP to a whole pound (how UPS bills), sorted, comma-joined. Every request
-- gets a row so the catch-up can resume from MAX(RequestId); IsMatchable = 0 marks requests that
-- aren't JSON, have an unreadable package weight, or whose ship-from matched no plant.
--
-- RateRequestServiceRate: one row per UPS service returned for a request, with its Published and
-- Negotiated Carrier Rates, read from the same JSON paths the Past Quotes report reads.
--
-- Running this script performs the one-time backfill at the end, which reads every row of
-- Requests_Rate and may take several minutes. After that, the reports call RecordRateRequestMatchKeys
-- on each load, which only reads requests newer than the last one recorded.

IF OBJECT_ID('dbo.RateRequestMatchKey', 'U') IS NULL
CREATE TABLE dbo.RateRequestMatchKey
(
    RequestId                 INT           NOT NULL,
    RequestDate               DATETIME      NULL,
    PlantCode                 VARCHAR(10)   NULL,
    ShipToZip                 VARCHAR(20)   NULL,
    PackageCount              INT           NULL,
    WeightsKey                VARCHAR(1000) NULL,        -- NULL (and not matchable) if the key would be longer
    ResidentialIndicatorSent  BIT           NOT NULL,
    IsMatchable               BIT           NOT NULL,
    CONSTRAINT PK_RateRequestMatchKey PRIMARY KEY (RequestId)
);
GO

IF INDEXPROPERTY(OBJECT_ID('dbo.RateRequestMatchKey'), 'IX_RateRequestMatchKey_Group', 'IndexID') IS NULL
CREATE INDEX IX_RateRequestMatchKey_Group ON dbo.RateRequestMatchKey (PlantCode, ShipToZip, PackageCount, WeightsKey, ResidentialIndicatorSent, RequestDate)
    WHERE IsMatchable = 1;
GO

-- Unfiltered: the Rate Changes range query wants matchable rows, but the competing-Quote lookup
-- (any same-plant request near a calculation) wants all of them.
IF INDEXPROPERTY(OBJECT_ID('dbo.RateRequestMatchKey'), 'IX_RateRequestMatchKey_PlantDate', 'IndexID') IS NULL
CREATE INDEX IX_RateRequestMatchKey_PlantDate ON dbo.RateRequestMatchKey (PlantCode, RequestDate)
    INCLUDE (ShipToZip, PackageCount, WeightsKey, ResidentialIndicatorSent, IsMatchable);
GO

IF OBJECT_ID('dbo.RateRequestServiceRate', 'U') IS NULL
CREATE TABLE dbo.RateRequestServiceRate
(
    RequestId       INT            NOT NULL,
    ServiceCode     VARCHAR(10)    NOT NULL,
    PublishedRate   DECIMAL(18,2)  NULL,
    NegotiatedRate  DECIMAL(18,2)  NULL,
    CONSTRAINT PK_RateRequestServiceRate PRIMARY KEY (RequestId, ServiceCode)
);
GO

-- Deliberately no new index on QuoteServiceCalculation: live quoting writes to it, and this script
-- doesn't add work to that path. If the reports' calculation lookup (by PlantCode + CreatedDate)
-- gets slow, an index there is the fix, as a separate decision.

CREATE OR ALTER PROCEDURE dbo.RecordRateRequestMatchKeys
AS
BEGIN
    SET NOCOUNT ON;

    -- Plant comes from RateRequestPlant, so catch that up first. It may skip if another caller holds
    -- its lock; then only go as far as it has got, and pick the rest up next time.
    EXEC dbo.RecordRateRequestPlants;

    BEGIN TRANSACTION;

    -- Only one caller catches up at a time; a concurrent caller skips rather than waits,
    -- so two report loads can't insert the same RequestId.
    DECLARE @lock INT;
    EXEC @lock = sp_getapplock @Resource = 'RecordRateRequestMatchKeys', @LockMode = 'Exclusive',
                               @LockOwner = 'Transaction', @LockTimeout = 0;
    IF @lock < 0
    BEGIN
        ROLLBACK TRANSACTION;
        RETURN;
    END

    DECLARE @after INT = ISNULL((SELECT MAX(RequestId) FROM dbo.RateRequestMatchKey), 0);
    DECLARE @upTo  INT = ISNULL((SELECT MAX(RequestId) FROM dbo.RateRequestPlant), 0);

    SELECT r.RequestId, r.RequestDate, r.RequestXml, r.ResponseXml, rp.PlantCode,
           CASE WHEN ISJSON(r.RequestXml) = 1 THEN 1 ELSE 0 END AS RequestIsJson,
           CASE WHEN ISJSON(r.ResponseXml) = 1 THEN 1 ELSE 0 END AS ResponseIsJson
    INTO #batch
    FROM dbo.Requests_Rate r
    JOIN dbo.RateRequestPlant rp ON rp.RequestId = r.RequestId
    WHERE r.RequestId > @after AND r.RequestId <= @upTo;

    -- Package weights: the app always sends Package as an array, but read a lone object too.
    -- Wide scale so the cast can't round a weight down before CEILING (4.0001 must bill as 5 lb).
    SELECT b.RequestId, TRY_CAST(w.Weight AS DECIMAL(18,6)) AS Weight
    INTO #weights
    FROM #batch b
    CROSS APPLY OPENJSON(
        CASE WHEN LEFT(LTRIM(JSON_QUERY(b.RequestXml, '$.RateRequest.Shipment.Package')), 1) = '['
             THEN JSON_QUERY(b.RequestXml, '$.RateRequest.Shipment.Package')
             ELSE '[' + JSON_QUERY(b.RequestXml, '$.RateRequest.Shipment.Package') + ']' END)
        WITH (Weight NVARCHAR(20) '$.PackageWeight.Weight') w
    WHERE b.RequestIsJson = 1
      AND JSON_QUERY(b.RequestXml, '$.RateRequest.Shipment.Package') IS NOT NULL;

    INSERT INTO dbo.RateRequestMatchKey
        (RequestId, RequestDate, PlantCode, ShipToZip, PackageCount, WeightsKey, ResidentialIndicatorSent, IsMatchable)
    SELECT b.RequestId,
           b.RequestDate,
           b.PlantCode,
           z.ShipToZip,
           pk.PackageCount,
           CASE WHEN LEN(pk.WeightsKey) <= 1000 THEN pk.WeightsKey END,
           -- Present (the app sends it as "") only when UPS classified the address Residential; see docs/issues/0002.
           CASE WHEN b.RequestIsJson = 1
                 AND JSON_VALUE(b.RequestXml, '$.RateRequest.Shipment.ShipTo.Address.ResidentialAddressIndicator') IS NOT NULL
                THEN 1 ELSE 0 END,
           CASE WHEN b.RequestIsJson = 1
                 AND b.PlantCode IS NOT NULL
                 AND NULLIF(z.ShipToZip, '') IS NOT NULL
                 AND pk.PackageCount > 0
                 AND pk.UnreadableWeights = 0
                 AND LEN(pk.WeightsKey) <= 1000
                THEN 1 ELSE 0 END
    FROM #batch b
    CROSS APPLY (
        SELECT CASE WHEN b.RequestIsJson = 1
                    THEN UPPER(LTRIM(RTRIM(JSON_VALUE(b.RequestXml, '$.RateRequest.Shipment.ShipTo.Address.PostalCode')))) END AS ShipToZip
    ) z
    OUTER APPLY (
        SELECT COUNT(*) AS PackageCount,
               SUM(CASE WHEN w.Weight IS NULL THEN 1 ELSE 0 END) AS UnreadableWeights,
               STRING_AGG(CAST(CAST(CEILING(w.Weight) AS INT) AS VARCHAR(MAX)), ',')
                   WITHIN GROUP (ORDER BY CEILING(w.Weight)) AS WeightsKey
        FROM #weights w
        WHERE w.RequestId = b.RequestId
    ) pk;

    -- One row per service code; if UPS ever listed a code twice, keep the first.
    INSERT INTO dbo.RateRequestServiceRate (RequestId, ServiceCode, PublishedRate, NegotiatedRate)
    SELECT RequestId, ServiceCode, PublishedRate, NegotiatedRate
    FROM (
        SELECT b.RequestId,
               s.ServiceCode,
               TRY_CAST(s.PublishedRate AS DECIMAL(18,2)) AS PublishedRate,
               TRY_CAST(s.NegotiatedRate AS DECIMAL(18,2)) AS NegotiatedRate,
               ROW_NUMBER() OVER (PARTITION BY b.RequestId, s.ServiceCode ORDER BY s.Ordinal) AS rn
        FROM #batch b
        CROSS APPLY (
            SELECT CAST(j.[key] AS INT) AS Ordinal,
                   JSON_VALUE(j.value, '$.Service.Code') AS ServiceCode,
                   JSON_VALUE(j.value, '$.TotalCharges.MonetaryValue') AS PublishedRate,
                   JSON_VALUE(j.value, '$.NegotiatedRateCharges.TotalCharge.MonetaryValue') AS NegotiatedRate
            FROM OPENJSON(
                CASE WHEN LEFT(LTRIM(JSON_QUERY(b.ResponseXml, '$.RateResponse.RatedShipment')), 1) = '['
                     THEN JSON_QUERY(b.ResponseXml, '$.RateResponse.RatedShipment')
                     ELSE '[' + JSON_QUERY(b.ResponseXml, '$.RateResponse.RatedShipment') + ']' END) j
        ) s
        WHERE b.ResponseIsJson = 1
          AND JSON_QUERY(b.ResponseXml, '$.RateResponse.RatedShipment') IS NOT NULL
          AND s.ServiceCode IS NOT NULL
    ) x
    WHERE x.rn = 1;

    DROP TABLE #weights;
    DROP TABLE #batch;

    COMMIT TRANSACTION;
END
GO

-- One-time backfill of existing requests.

-- ---------------------------------------------------------------------------------------------
-- Verification queries (run by hand; the app never runs these).
-- ---------------------------------------------------------------------------------------------
--
-- 1. Extraction spot check: compare a sample against the Past Quotes page for the same RequestIds
--    (ship-to zip, weights, and each service's Published/Negotiated rate should agree).
--
--    SELECT TOP 20 k.*, s.ServiceCode, s.PublishedRate, s.NegotiatedRate
--    FROM dbo.RateRequestMatchKey k
--    LEFT JOIN dbo.RateRequestServiceRate s ON s.RequestId = k.RequestId
--    WHERE k.IsMatchable = 1
--    ORDER BY k.RequestId DESC;
--
-- 2. Calculated Rate timing: how long after a UPS Quote are its QuoteServiceCalculation rows logged?
--    The report attaches a calculation only if it's logged 0-30 seconds after the request
--    (RateChanges.CalculationWindow). If many gaps fall just outside 30s, widen the window.
--
--    SELECT gap.Seconds, COUNT(*) AS Calculations
--    FROM dbo.QuoteServiceCalculation c
--    CROSS APPLY (
--        SELECT TOP 1 DATEDIFF(SECOND, k.RequestDate, c.CreatedDate) AS Seconds
--        FROM dbo.RateRequestMatchKey k
--        WHERE k.PlantCode = c.PlantCode
--          AND k.RequestDate <= c.CreatedDate
--          AND k.RequestDate >= DATEADD(MINUTE, -5, c.CreatedDate)
--        ORDER BY k.RequestDate DESC
--    ) gap
--    WHERE c.Carrier = 'UPS'
--    GROUP BY gap.Seconds
--    ORDER BY gap.Seconds;

IF OBJECT_ID('dbo.Consumer', 'U') IS NULL
CREATE TABLE dbo.Consumer
(
    ConsumerId   INT IDENTITY(1,1)  NOT NULL,
    Name         VARCHAR(100)       NOT NULL,
    ApiKeyHash   VARBINARY(32)      NOT NULL,   -- SHA-256 of the API key
    IsActive     BIT                NOT NULL    CONSTRAINT DF_Consumer_IsActive DEFAULT (1),
    CreatedDate  DATETIME           NOT NULL    CONSTRAINT DF_Consumer_CreatedDate DEFAULT (GETDATE()),
    CONSTRAINT PK_Consumer PRIMARY KEY (ConsumerId),
    CONSTRAINT UQ_Consumer_Name UNIQUE (Name),
    CONSTRAINT UQ_Consumer_ApiKeyHash UNIQUE (ApiKeyHash)
);
GO

-- QuoteId is generated by the application, not a database identity, so every carrier's logging can share it
-- (docs/adr/0002-app-generated-quote-id.md).
IF OBJECT_ID('dbo.Quote', 'U') IS NULL
CREATE TABLE dbo.Quote
(
    QuoteId          UNIQUEIDENTIFIER  NOT NULL,
    ConsumerId       INT               NOT NULL    CONSTRAINT FK_Quote_Consumer REFERENCES dbo.Consumer (ConsumerId),
    ReferenceNumber  VARCHAR(100)      NOT NULL,   -- what the Consumer calls this shipment; also on its Invoice (ADR 0001)
    PlantCode        VARCHAR(10)       NOT NULL,
    RequestJson      NVARCHAR(MAX)     NOT NULL,   -- the request as the Consumer sent it
    CreatedDate      DATETIME          NOT NULL    CONSTRAINT DF_Quote_CreatedDate DEFAULT (GETDATE()),
    CONSTRAINT PK_Quote PRIMARY KEY (QuoteId),
    INDEX IX_Quote_ReferenceNumber (ReferenceNumber),
    INDEX IX_Quote_ConsumerId (ConsumerId, CreatedDate)
);
GO

CREATE OR ALTER PROCEDURE dbo.LogQuote
    @QuoteId          UNIQUEIDENTIFIER,
    @ConsumerId       INT,
    @ReferenceNumber  VARCHAR(100),
    @PlantCode        VARCHAR(10),
    @RequestJson      NVARCHAR(MAX)
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO dbo.Quote (QuoteId, ConsumerId, ReferenceNumber, PlantCode, RequestJson)
    VALUES (@QuoteId, @ConsumerId, @ReferenceNumber, @PlantCode, @RequestJson);
END
GO

GO

-- -----------------------------------------------------------------------------------------------------
-- Part 4: CostPlus (FileMaker) request table, in CostPlus's layout, and the poller's own tables (#31, ADR 0005)
--
-- One row holds both the request and the result. The poller keeps FileMaker's layout inside its adapter. Column
-- names and types are as read from UPSRate.dbo.FreightRequests_SANDBOX. Not read: that table's primary key (so
-- there is none here; the claim table is the guard against processing a row twice) and the precision of the
-- Rate columns (DECIMAL(18,2) here; confirm).
-- -----------------------------------------------------------------------------------------------------
IF OBJECT_ID('dbo.FreightRequests', 'U') IS NULL
CREATE TABLE dbo.FreightRequests
(
    LoginId                  VARCHAR(50)    NOT NULL,
    QtyNumber                TINYINT        NOT NULL,
    ToAddress                VARCHAR(200)   NULL,
    ToCity                   VARCHAR(200)   NULL,
    ToState                  VARCHAR(50)    NULL,
    ToZip                    VARCHAR(50)    NULL,
    ToCountry                VARCHAR(2)     NULL,
    FromAddress              VARCHAR(200)   NULL,
    FromCity                 VARCHAR(200)   NULL,
    FromState                VARCHAR(50)    NULL,
    FromZip                  VARCHAR(50)    NULL,
    FromCountry              VARCHAR(2)     NULL,
    NumPackages              INT            NULL,
    PkgWeight                INT            NULL,
    LastPkgWeight            INT            NULL,
    SignatureRequired        VARCHAR(50)    NULL,
    RequestUPS               BIT            NULL,
    RequestLTL               BIT            NULL,
    FreightClass             VARCHAR(50)    NULL,
    PickupDate               VARCHAR(50)    NULL,
    NotifyBeforeDelivery     BIT            NULL,
    LiftgatePickup           BIT            NULL,
    LiftgateDelivery         BIT            NULL,
    LimitedAccessPickup      BIT            NULL,
    LimitedAccessDelivery    BIT            NULL,
    ResidentialPickup        BIT            NULL,
    ResidentialDelivery      BIT            NULL,
    InsidePickup             BIT            NULL,
    InsideDelivery           BIT            NULL,
    SortAndSegregate         BIT            NULL,
    StopoffCharge            BIT            NULL,
    DateRequested            DATETIME       NULL,
    DateProcessed            DATETIME       NULL,
    Carrier01                VARCHAR(50)    NULL,
    Service01                VARCHAR(50)    NULL,
    ServiceCode01            VARCHAR(50)    NULL,
    Rate01                   DECIMAL(18,2)  NULL,
    Carrier02                VARCHAR(50)    NULL,
    Service02                VARCHAR(50)    NULL,
    ServiceCode02            VARCHAR(50)    NULL,
    Rate02                   DECIMAL(18,2)  NULL,
    Carrier03                VARCHAR(50)    NULL,
    Service03                VARCHAR(50)    NULL,
    ServiceCode03            VARCHAR(50)    NULL,
    Rate03                   DECIMAL(18,2)  NULL,
    Carrier04                VARCHAR(50)    NULL,
    Service04                VARCHAR(50)    NULL,
    ServiceCode04            VARCHAR(50)    NULL,
    Rate04                   DECIMAL(18,2)  NULL,
    Carrier05                VARCHAR(50)    NULL,
    Service05                VARCHAR(50)    NULL,
    ServiceCode05            VARCHAR(50)    NULL,
    Rate05                   DECIMAL(18,2)  NULL,
    Carrier06                VARCHAR(50)    NULL,
    Service06                VARCHAR(50)    NULL,
    ServiceCode06            VARCHAR(50)    NULL,
    Rate06                   DECIMAL(18,2)  NULL,
    Carrier07                VARCHAR(50)    NULL,
    Service07                VARCHAR(50)    NULL,
    ServiceCode07            VARCHAR(50)    NULL,
    Rate07                   DECIMAL(18,2)  NULL,
    Carrier08                VARCHAR(50)    NULL,
    Service08                VARCHAR(50)    NULL,
    ServiceCode08            VARCHAR(50)    NULL,
    Rate08                   DECIMAL(18,2)  NULL,
    Carrier09                VARCHAR(50)    NULL,
    Service09                VARCHAR(50)    NULL,
    ServiceCode09            VARCHAR(50)    NULL,
    Rate09                   DECIMAL(18,2)  NULL,
    Carrier10                VARCHAR(50)    NULL,
    Service10                VARCHAR(50)    NULL,
    ServiceCode10            VARCHAR(50)    NULL,
    Rate10                   DECIMAL(18,2)  NULL,
    Carrier11                VARCHAR(50)    NULL,
    Service11                VARCHAR(50)    NULL,
    ServiceCode11            VARCHAR(50)    NULL,
    Rate11                   DECIMAL(18,2)  NULL,
    Carrier12                VARCHAR(50)    NULL,
    Service12                VARCHAR(50)    NULL,
    ServiceCode12            VARCHAR(50)    NULL,
    Rate12                   DECIMAL(18,2)  NULL,
    Carrier13                VARCHAR(50)    NULL,
    Service13                VARCHAR(50)    NULL,
    ServiceCode13            VARCHAR(50)    NULL,
    Rate13                   DECIMAL(18,2)  NULL,
    Carrier14                VARCHAR(50)    NULL,
    Service14                VARCHAR(50)    NULL,
    ServiceCode14            VARCHAR(50)    NULL,
    Rate14                   DECIMAL(18,2)  NULL,
    Carrier15                VARCHAR(50)    NULL,
    Service15                VARCHAR(50)    NULL,
    ServiceCode15            VARCHAR(50)    NULL,
    Rate15                   DECIMAL(18,2)  NULL,
    ErrorCode01              VARCHAR(10)    NULL,
    ErrorDesc01              VARCHAR(1000)  NULL,
    ErrorCode02              VARCHAR(10)    NULL,
    ErrorDesc02              VARCHAR(1000)  NULL,
    ErrorCode03              VARCHAR(10)    NULL,
    ErrorDesc03              VARCHAR(1000)  NULL,
    DateQueued               DATETIME       NULL,
    DateRated                DATETIME       NULL,
    LastQuote                INT            NULL,
    ClassifiedResidential    BIT            NULL,
    SubstitutedZip           VARCHAR(50)    NULL,
    Note01                   VARCHAR(200)   NULL,
    Note02                   VARCHAR(200)   NULL,
    Note03                   VARCHAR(200)   NULL,
    Note04                   VARCHAR(200)   NULL,
    Note05                   VARCHAR(200)   NULL,
    Note06                   VARCHAR(200)   NULL,
    Note07                   VARCHAR(200)   NULL,
    Note08                   VARCHAR(200)   NULL,
    Note09                   VARCHAR(200)   NULL,
    Note10                   VARCHAR(200)   NULL,
    Note11                   VARCHAR(200)   NULL,
    Note12                   VARCHAR(200)   NULL,
    Note13                   VARCHAR(200)   NULL,
    Note14                   VARCHAR(200)   NULL,
    Note15                   VARCHAR(200)   NULL,
    AcctNumber               INT            NULL,
    ShipWithArray            VARCHAR(256)   NULL
);
GO

IF INDEXPROPERTY(OBJECT_ID('dbo.FreightRequests'), 'IX_FreightRequests_Key', 'IndexID') IS NULL
CREATE INDEX IX_FreightRequests_Key ON dbo.FreightRequests (LoginId, QtyNumber, DateRequested);
GO

-- The claim: the guard that a request is processed once (user stories 19 to 21). Unique on the request's
-- LoginId, QtyNumber and DateRequested (provisional, ADR 0005), so a resubmission with a new DateRequested is a
-- new request. The tracer (#31) only inserts the claim: the unique key is the guard. Taking a claim older than
-- about 2 minutes that is not completed (with one atomic UPDATE ... OUTPUT, ADR 0005) comes with #32.
-- QuoteId ties the request to its Quote (dbo.Quote).
IF OBJECT_ID('dbo.CostPlusRequestClaim', 'U') IS NULL
CREATE TABLE dbo.CostPlusRequestClaim
(
    ClaimId        INT IDENTITY(1,1)  NOT NULL,
    LoginId        VARCHAR(50)        NOT NULL,
    QtyNumber      TINYINT            NOT NULL,
    DateRequested  DATETIME           NOT NULL,
    ClaimedBy      VARCHAR(100)       NOT NULL,        -- which poller instance holds the claim
    ClaimedAt      DATETIME2(3)       NOT NULL,        -- refreshed when a stale claim is taken again
    CompletedAt    DATETIME2(3)       NULL,            -- set with the result write; NULL while in flight
    QuoteId        UNIQUEIDENTIFIER   NULL,
    CONSTRAINT PK_CostPlusRequestClaim PRIMARY KEY (ClaimId),
    CONSTRAINT UQ_CostPlusRequestClaim_Request UNIQUE (LoginId, QtyNumber, DateRequested)
);
GO

-- The Reference Number the poller generates for a CostPlus request (ADR 0005): CostPlus supplies none this
-- phase. Stored on the Quote as text (dbo.Quote.ReferenceNumber).
IF OBJECT_ID('dbo.CostPlusReferenceNumber', 'SO') IS NULL
CREATE SEQUENCE dbo.CostPlusReferenceNumber AS INT START WITH 1 INCREMENT BY 1;
GO

-- The fixed "CostPlus" Consumer: the poller is in-process, so it has no API key. ApiKeyHash is NOT NULL and
-- unique, so it gets the hash of a random value nobody ever sees: no key can authenticate as this Consumer.
IF NOT EXISTS (SELECT 1 FROM dbo.Consumer WHERE Name = 'CostPlus')
    INSERT INTO dbo.Consumer (Name, ApiKeyHash)
    VALUES ('CostPlus', HASHBYTES('SHA2_256', CONVERT(VARCHAR(36), NEWID()) + CONVERT(VARCHAR(36), NEWID())));
GO

-- The observe-only shadow run (#36, ADR 0005): for each request the console app answered, the poller's own result next to
-- the console app's, both as JSON in the shape FileMaker reads (CostPlusShadowComparison). Explanation is filled in by a
-- person: a divergence with none is Unexplained and still needs looking at.
IF OBJECT_ID('dbo.CostPlusShadowResult', 'U') IS NULL
CREATE TABLE dbo.CostPlusShadowResult
(
    ShadowResultId  INT IDENTITY(1,1)  NOT NULL,
    LoginId         VARCHAR(50)        NOT NULL,
    QtyNumber       TINYINT            NOT NULL,
    DateRequested   DATETIME           NOT NULL,
    ComparedAt      DATETIME2(3)       NOT NULL    CONSTRAINT DF_CostPlusShadowResult_ComparedAt DEFAULT (SYSDATETIME()),
    ConsoleResult   NVARCHAR(MAX)      NOT NULL,   -- what the console app wrote to the request's row
    PollerResult    NVARCHAR(MAX)      NOT NULL,   -- what the poller would have written
    Diverged        BIT                NOT NULL,
    Differences     NVARCHAR(MAX)      NULL,       -- one line per difference; NULL when they agree
    Explanation     NVARCHAR(1000)     NULL,       -- why a divergence is expected (set by hand)
    Status AS (CASE WHEN Diverged = 0 THEN 'Match' WHEN Explanation IS NULL THEN 'Unexplained' ELSE 'Explained' END),
    CONSTRAINT PK_CostPlusShadowResult PRIMARY KEY (ShadowResultId),
    CONSTRAINT UQ_CostPlusShadowResult_Request UNIQUE (LoginId, QtyNumber, DateRequested)
);
GO

-- Who recorded the explanation, and when (#45): the Windows username of the reviewer who saved it. Both are NULL until an
-- explanation is saved, and cleared with it.
IF COL_LENGTH('dbo.CostPlusShadowResult', 'ExplainedBy') IS NULL
    ALTER TABLE dbo.CostPlusShadowResult ADD ExplainedBy NVARCHAR(256) NULL;
GO
IF COL_LENGTH('dbo.CostPlusShadowResult', 'ExplainedAt') IS NULL
    ALTER TABLE dbo.CostPlusShadowResult ADD ExplainedAt DATETIME2(3) NULL;
GO

SET NOEXEC OFF;
GO

-- Confirm what now exists in the current database; every row should say OK.
SELECT DB_NAME() AS [Database], o.name AS Expected, CASE WHEN x.object_id IS NULL THEN 'MISSING' ELSE 'OK' END AS Status
FROM (VALUES ('Plants'), ('PlantCarrierCharges2017'), ('Requests_Rate'), ('Log_LTL'), ('PiedmontAcctsForSpecialAlpRates'),
             ('GetPlantCharges'), ('LogRequest_Rate'), ('LogResultsLTL'),
             ('QuoteServiceCalculation'), ('LogQuoteServiceCalculation'),
             ('CalculatedRateShadowLog'), ('LogCalculatedRateShadowComparison'),
             ('RateRequestPlant'), ('RecordRateRequestPlants'), ('RateRequestMatchKey'), ('RateRequestServiceRate'), ('RecordRateRequestMatchKeys'),
             ('Consumer'), ('Quote'), ('LogQuote'),
             ('FreightRequests'), ('CostPlusRequestClaim'), ('CostPlusReferenceNumber'), ('CostPlusShadowResult')) o(name)
LEFT JOIN sys.objects x ON x.name = o.name AND x.schema_id = SCHEMA_ID('dbo');
GO
