# The Quote Log reads production UPSRate through a read-only login

**Status:** accepted.

The Quote Log shows what production has actually answered, so it reads the production `UPSRate` database on AZUREDB01\AZUREDB01 (`Requests_Rate` and `Log_LTL`). This is the only place the app connects to UPSRate. It does so through its own connection setting (`UpsRateReadOnly`) and a SQL login that has read rights only. The app never writes to UPSRate, no script or procedure uses a three-part `[UpsRate]` name, and tests never connect to it. The main connection keeps its FreightEstimator-only guard; the Quote Log's connection is a named exception to it.

The alternative was copying production rows into the FreightEstimator database and reporting from the copy. It was rejected because a copy is always behind, needs a job that reads UPSRate anyway, and a report that looks current but is not is worse than a slower one that is.

Consequences a future reader should expect:

- UPSRate's tables have no indexes and may never be changed by this app, so the report requires a date range (at most 31 days) and scans by `RequestDate`. If it gets slow, whether to add an index is the DBA's decision, not this app's.
- The helper tables in UPSRate (`RateRequestPlant`, `RateRequestMatchKey`) are stale or empty and nothing updates them, so the report does not use them. It reads plant and ship-to ZIP from the request JSON.
- UPSRate has no `Quote` or `Consumer` table, so a production Quote has no origin. The report shows who requested it (`UserName`) and nothing more.
- `QuoteServiceCalculation` in UPSRate holds almost nothing, so the report shows UPS's own rates only, never a Calculated Rate.
- The read-only login is created by a person with the rights to do it, not by the app or by Claude. The setting is empty by default, and the report then says it is not configured instead of failing.

This does not reverse the rule that production writes go nowhere near UPSRate, and it does not make Past Quotes a production report: Past Quotes still reads the FreightEstimator test database.
