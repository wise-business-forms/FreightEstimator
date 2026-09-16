-- Target database: UpsRate (same database as QuoteServiceCalculation, Requests_Rate, LogResultsLTL)
--
-- Purely additive, and purely diagnostic: this script does not alter any existing table or
-- stored procedure. It creates one new, throwaway table and one new stored procedure so the
-- Deepen Calculated Rate migration (docs/adr/0003-deepen-calculated-rate-via-shadow-mode.md)
-- can record, per quote/service/call-site, the value today's existing formula produced next to
-- the value the new CalculatedRate.Apply module produced, without changing what's persisted in
-- QuoteServiceCalculation or shown on screen. Intended to be dropped once all four call sites
-- listed in docs/issues/0001-ups-markup-divergence.md are migrated and live.

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

CREATE INDEX IX_CalculatedRateShadowLog_CallSite ON dbo.CalculatedRateShadowLog (CallSite);
GO

CREATE PROCEDURE dbo.LogCalculatedRateShadowComparison
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
