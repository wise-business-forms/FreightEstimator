-- Target database: UpsRate (same database as Requests_Rate and Plants)
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

CREATE TABLE dbo.RateRequestPlant
(
    RequestId   INT          NOT NULL,
    PlantCode   VARCHAR(10)  NULL,        -- NULL: ship-from didn't match an active plant, or the request isn't JSON
    CONSTRAINT PK_RateRequestPlant PRIMARY KEY (RequestId)
);
GO

CREATE INDEX IX_RateRequestPlant_PlantCode ON dbo.RateRequestPlant (PlantCode, RequestId);
GO

CREATE PROCEDURE dbo.RecordRateRequestPlants
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
EXEC dbo.RecordRateRequestPlants;
GO
