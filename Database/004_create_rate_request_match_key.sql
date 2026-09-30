-- Target database: UpsRate (same database as Requests_Rate, RateRequestPlant and QuoteServiceCalculation)
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

CREATE INDEX IX_RateRequestMatchKey_Group
    ON dbo.RateRequestMatchKey (PlantCode, ShipToZip, PackageCount, WeightsKey, ResidentialIndicatorSent, RequestDate)
    WHERE IsMatchable = 1;
GO

-- Unfiltered: the Rate Changes range query wants matchable rows, but the competing-Quote lookup
-- (any same-plant request near a calculation) wants all of them.
CREATE INDEX IX_RateRequestMatchKey_PlantDate
    ON dbo.RateRequestMatchKey (PlantCode, RequestDate)
    INCLUDE (ShipToZip, PackageCount, WeightsKey, ResidentialIndicatorSent, IsMatchable);
GO

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

CREATE PROCEDURE dbo.RecordRateRequestMatchKeys
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
EXEC dbo.RecordRateRequestMatchKeys;
GO

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
