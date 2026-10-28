-- Target database: UpsRate (run on BOTH the production instance and SANDBOX, then diff the two outputs)
--
-- Read-only. Lists the structure (not the data) of every database object the quote path, the
-- logging and the reports touch, one sorted line per column or parameter, so two databases can be
-- compared with a text diff. Data values may differ between environments; this output must not.
-- Used by docs/issues for "Per-environment data connection strings" and the SANDBOX baseline.

SET NOCOUNT ON;

SELECT line FROM (

-- Tables: one line per column
SELECT 'TABLE '  + t.name + '.' + c.name
     + ' ' + CASE WHEN ty.name = 'numeric' THEN 'decimal' ELSE ty.name END   -- numeric and decimal are the same type
     + CASE WHEN ty.name IN ('varchar','nvarchar','char','nchar','varbinary')
            THEN '(' + CASE WHEN c.max_length = -1 THEN 'max'
                            WHEN ty.name IN ('nvarchar','nchar') THEN CAST(c.max_length / 2 AS VARCHAR(10))
                            ELSE CAST(c.max_length AS VARCHAR(10)) END + ')'
            WHEN ty.name IN ('decimal','numeric')
            THEN '(' + CAST(c.precision AS VARCHAR(10)) + ',' + CAST(c.scale AS VARCHAR(10)) + ')'
            ELSE '' END
     + CASE WHEN c.is_nullable = 1 THEN ' NULL' ELSE ' NOT NULL' END
     + CASE WHEN c.is_identity = 1 THEN ' IDENTITY' ELSE '' END AS line
FROM sys.tables t
JOIN sys.columns c  ON c.object_id = t.object_id
JOIN sys.types ty   ON ty.user_type_id = c.user_type_id
WHERE t.name IN ('Plants', 'PlantCarrierCharges2017', 'Requests_Rate',
                 'QuoteServiceCalculation', 'CalculatedRateShadowLog',
                 'RateRequestPlant', 'RateRequestMatchKey', 'RateRequestServiceRate',
                 'Log_LTL', 'PiedmontAcctsForSpecialAlpRates')

UNION ALL

-- Stored procedures: one line per parameter, or one line if the procedure takes none
SELECT 'PROC ' + p.name + '.' + ISNULL(pa.name, '(no parameters)')
     + ISNULL(' ' + ty.name
     + CASE WHEN ty.name IN ('varchar','nvarchar','char','nchar')
            THEN '(' + CASE WHEN pa.max_length = -1 THEN 'max'
                            WHEN ty.name IN ('nvarchar','nchar') THEN CAST(pa.max_length / 2 AS VARCHAR(10))
                            ELSE CAST(pa.max_length AS VARCHAR(10)) END + ')'
            WHEN ty.name IN ('decimal','numeric')
            THEN '(' + CAST(pa.precision AS VARCHAR(10)) + ',' + CAST(pa.scale AS VARCHAR(10)) + ')'
            ELSE '' END, '')
FROM sys.procedures p
LEFT JOIN sys.parameters pa ON pa.object_id = p.object_id
LEFT JOIN sys.types ty      ON ty.user_type_id = pa.user_type_id
WHERE p.name IN ('GetPlantCharges', 'LogResultsLTL', 'LogRequest_Rate', 'Log_Quote',
                 'LogQuoteServiceCalculation', 'LogCalculatedRateShadowComparison',
                 'RecordRateRequestPlants', 'RecordRateRequestMatchKeys')

UNION ALL

-- Procedure bodies: a hash of each definition with whitespace removed, so a body that differs
-- between environments shows up even when its signature is identical. A mismatch means "look at
-- this procedure", not necessarily "it is wrong".
SELECT 'PROCBODY ' + p.name + ' ' + CONVERT(VARCHAR(64), HASHBYTES('SHA2_256',
           REPLACE(REPLACE(REPLACE(REPLACE(OBJECT_DEFINITION(p.object_id), CHAR(13), ''), CHAR(10), ''), CHAR(9), ''), ' ', '')), 2)
FROM sys.procedures p
WHERE p.name IN ('GetPlantCharges', 'LogResultsLTL', 'LogRequest_Rate', 'Log_Quote',
                 'LogQuoteServiceCalculation', 'LogCalculatedRateShadowComparison',
                 'RecordRateRequestPlants', 'RecordRateRequestMatchKeys')

) AS fingerprint
-- Byte-ordered so both servers sort identically whatever their collation.
ORDER BY line COLLATE Latin1_General_BIN2;
