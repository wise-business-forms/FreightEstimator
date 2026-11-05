# Generate the Quote ID in the app, not from a database identity

To attach per-service Calculated Rate breakdown rows to a specific Quote, we need one key that works across both carrier logging paths. UPS already has one — `LogRequest_Rate` returns an `@RequestId` output parameter — but the Logistics Partner path (`LogResultsLTL`) has no equivalent; it's a fire-and-forget `ExecuteNonQuery()` with no output parameter at all.

We considered adding an output identity parameter to `LogResultsLTL`, mirroring UPS's existing pattern. We rejected it because it would leave the two carriers keyed by two different mechanisms indefinitely, working against the separate goal of eventually unifying UPS and Logistics Partner Quote History into one reporting view.

Instead, the app generates a single Quote ID itself before calling either carrier's logging path, and passes it in as an ordinary input parameter to both. Both `Requests_Rate` and the Logistics Partner log table gain this new column; neither stored procedure needs new output-parameter handling.
