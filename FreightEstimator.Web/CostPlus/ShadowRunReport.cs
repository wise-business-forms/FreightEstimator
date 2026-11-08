using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using Newtonsoft.Json;

namespace AuthenticationServer.CostPlus
{
    /// <summary>
    /// What the Reports section's Poller Shadow Run pages read from dbo.CostPlusShadowResult and dbo.FreightRequests (#40), and the
    /// one write it makes: a reviewer's explanation. Throwaway, removed with the table after the cutover.
    /// </summary>
    internal static class ShadowRunReport
    {
        public const string Match = "Match", Explained = "Explained", Unexplained = "Unexplained";
        public const int ExplanationMaxLength = 1000;

        public class Day
        {
            public DateTime Date;
            public int Match, Explained, Unexplained, NotCompared;
            public int Compared => Match + Explained + Unexplained;
            /// <summary>Match divided by compared; Explained does not count as a match. Null when nothing was compared.</summary>
            public decimal? MatchRate => Compared == 0 ? (decimal?)null : (decimal)Match / Compared;
        }

        public class Summary
        {
            public List<Day> Days;
            /// <summary>The request date of the latest Unexplained comparison, over all time, or null.</summary>
            public DateTime? LatestUnexplainedDate;
            /// <summary>When the latest comparison was made (database clock), over all time, or null.</summary>
            public DateTime? LatestComparedAt;
        }

        /// <summary>One row per calendar day from <paramref name="from"/> to <paramref name="to"/> inclusive, newest first, with days that have nothing included.</summary>
        public static Summary LoadSummary(SqlConnection conn, DateTime from, DateTime to)
        {
            var summary = new Summary();
            var days = new SortedDictionary<DateTime, Day>();
            for (DateTime d = from.Date; d <= to.Date; d = d.AddDays(1)) days[d] = new Day { Date = d };

            using (var cmd = new SqlCommand(
                "SELECT CAST(s.DateRequested AS DATE), s.Status, COUNT(*) FROM dbo.CostPlusShadowResult s " +
                "WHERE s.DateRequested >= @from AND s.DateRequested < @toExclusive AND s.LoginId NOT LIKE @probe + '%' " +
                "GROUP BY CAST(s.DateRequested AS DATE), s.Status", conn))
            {
                AddRange(cmd, from, to);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        Day day = days[r.GetDateTime(0)];
                        int n = r.GetInt32(2);
                        switch (r.GetString(1))
                        {
                            case Match: day.Match += n; break;
                            case Explained: day.Explained += n; break;
                            default: day.Unexplained += n; break;
                        }
                    }
                }
            }

            // Eligible requests with no comparison: the poller's own filter (CostPlusPollCycle.ShadowEligibleWhere), minus its 24-hour window.
            using (var cmd = new SqlCommand(
                "SELECT CAST(f.DateRequested AS DATE), COUNT(*) FROM dbo.FreightRequests f " +
                "WHERE " + CostPlusPollCycle.ShadowEligibleWhere + " AND " + CostPlusPollCycle.NotComparedWhere +
                "  AND f.DateRequested >= @from AND f.DateRequested < @toExclusive " +
                "GROUP BY CAST(f.DateRequested AS DATE)", conn))
            {
                AddRange(cmd, from, to);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read()) days[r.GetDateTime(0)].NotCompared = r.GetInt32(1);
                }
            }

            using (var cmd = new SqlCommand(
                "SELECT (SELECT MAX(CAST(DateRequested AS DATE)) FROM dbo.CostPlusShadowResult WHERE Status = 'Unexplained' AND LoginId NOT LIKE @probe + '%'), " +
                "       (SELECT MAX(ComparedAt) FROM dbo.CostPlusShadowResult WHERE LoginId NOT LIKE @probe + '%')", conn))
            {
                cmd.Parameters.Add("@probe", SqlDbType.VarChar, 50).Value = CostPlusProbe.LoginIdPrefix;
                using (var r = cmd.ExecuteReader())
                {
                    r.Read();
                    summary.LatestUnexplainedDate = r.IsDBNull(0) ? (DateTime?)null : r.GetDateTime(0);
                    summary.LatestComparedAt = r.IsDBNull(1) ? (DateTime?)null : r.GetDateTime(1);
                }
            }

            summary.Days = days.Values.Reverse().ToList();
            return summary;
        }

        private static void AddRange(SqlCommand cmd, DateTime from, DateTime to)
        {
            cmd.Parameters.Add("@from", SqlDbType.DateTime).Value = from.Date;
            cmd.Parameters.Add("@toExclusive", SqlDbType.DateTime).Value = to.Date.AddDays(1);
            cmd.Parameters.Add("@probe", SqlDbType.VarChar, 50).Value = CostPlusProbe.LoginIdPrefix;
        }

        public class ListRow
        {
            public int Id;
            public string LoginId, Status;
            public DateTime DateRequested;
        }

        /// <summary>The day's Shadow Comparisons, oldest first, optionally only one status.</summary>
        public static List<ListRow> LoadDay(SqlConnection conn, DateTime day, string status)
        {
            var rows = new List<ListRow>();
            using (var cmd = new SqlCommand(
                "SELECT s.ShadowResultId, s.LoginId, s.DateRequested, s.Status FROM dbo.CostPlusShadowResult s " +
                "WHERE s.DateRequested >= @from AND s.DateRequested < @toExclusive AND s.LoginId NOT LIKE @probe + '%' " +
                (status != null ? "AND s.Status = @status " : "") +
                "ORDER BY s.DateRequested, s.LoginId, s.ShadowResultId", conn))
            {
                AddRange(cmd, day, day);
                if (status != null) cmd.Parameters.Add("@status", SqlDbType.VarChar, 20).Value = status;
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                        rows.Add(new ListRow { Id = r.GetInt32(0), LoginId = r.GetString(1), DateRequested = r.GetDateTime(2), Status = r.GetString(3) });
                }
            }
            return rows;
        }

        /// <summary>The status a list may be filtered to, or null for all. Anything else is no filter.</summary>
        public static string ParseStatus(string status) =>
            new[] { Match, Explained, Unexplained }.FirstOrDefault(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));

        public class Detail
        {
            public int Id;
            public string LoginId, Status, Explanation, ExplainedBy;
            public byte QtyNumber;
            public DateTime DateRequested, ComparedAt;
            public DateTime? ExplainedAt;
            public bool Diverged;
            public List<string> Differences = new List<string>();
            public CostPlusShadowComparison.Result Console, Poller;
            /// <summary>The request's inputs as label/value pairs, or null when its row has been purged.</summary>
            public List<KeyValuePair<string, string>> RequestInputs;
        }

        public static Detail LoadDetail(SqlConnection conn, int id)
        {
            Detail detail;
            using (var cmd = new SqlCommand(
                "SELECT LoginId, QtyNumber, DateRequested, ComparedAt, ConsoleResult, PollerResult, Diverged, Differences, Explanation, Status, ExplainedBy, ExplainedAt " +
                "FROM dbo.CostPlusShadowResult WHERE ShadowResultId = @id", conn))
            {
                cmd.Parameters.Add("@id", SqlDbType.Int).Value = id;
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return null;
                    detail = new Detail
                    {
                        Id = id, LoginId = r.GetString(0), QtyNumber = r.GetByte(1), DateRequested = r.GetDateTime(2), ComparedAt = r.GetDateTime(3),
                        Console = JsonConvert.DeserializeObject<CostPlusShadowComparison.Result>(r.GetString(4)) ?? new CostPlusShadowComparison.Result(),
                        Poller = JsonConvert.DeserializeObject<CostPlusShadowComparison.Result>(r.GetString(5)) ?? new CostPlusShadowComparison.Result(),
                        Diverged = r.GetBoolean(6),
                        Explanation = r.IsDBNull(8) ? null : r.GetString(8), Status = r.GetString(9),
                        ExplainedBy = r.IsDBNull(10) ? null : r.GetString(10), ExplainedAt = r.IsDBNull(11) ? (DateTime?)null : r.GetDateTime(11)
                    };
                    if (!r.IsDBNull(7))
                        detail.Differences = r.GetString(7).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).ToList();
                }
            }

            using (var cmd = new SqlCommand("SELECT * FROM dbo.FreightRequests WHERE LoginId = @l AND QtyNumber = @q AND DateRequested = @d", conn))
            {
                cmd.Parameters.Add("@l", SqlDbType.VarChar, 50).Value = detail.LoginId;
                cmd.Parameters.Add("@q", SqlDbType.TinyInt).Value = detail.QtyNumber;
                cmd.Parameters.Add("@d", SqlDbType.DateTime).Value = detail.DateRequested;
                using (var r = cmd.ExecuteReader())
                {
                    if (r.Read()) detail.RequestInputs = ReadInputs(r);
                }
            }
            return detail;
        }

        /// <summary>The columns that hold the answer, not the question: the comparison shows those, so the inputs leave them out.</summary>
        private static readonly System.Text.RegularExpressions.Regex ResultColumn = new System.Text.RegularExpressions.Regex(
            @"^((Carrier|Service|Rate|Note|ErrorCode|ErrorDesc)\d\d|DateProcessed|DateRated|ClassifiedResidential|SubstitutedZip)$");

        /// <summary>Every request column that says what was asked and has a value, in table order.</summary>
        private static List<KeyValuePair<string, string>> ReadInputs(SqlDataReader r)
        {
            var inputs = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < r.FieldCount; i++)
            {
                string column = r.GetName(i);
                if (ResultColumn.IsMatch(column) || r.IsDBNull(i)) continue;
                inputs.Add(new KeyValuePair<string, string>(column, Convert.ToString(r.GetValue(i), System.Globalization.CultureInfo.InvariantCulture)));
            }
            return inputs;
        }

        public enum ExplainOutcome { Saved, Cleared, EmptyRejected, TooLongRejected, NotFound }

        /// <summary>
        /// Records, edits or clears the explanation of a diverged comparison. An explanation is required and at most
        /// <see cref="ExplanationMaxLength"/> characters after trimming; an empty one on an already-explained comparison clears it
        /// (and ExplainedBy and ExplainedAt with it). A Match has nothing to explain and is NotFound here. Writes only dbo.CostPlusShadowResult.
        /// </summary>
        public static ExplainOutcome Explain(SqlConnection conn, int id, string explanation, string explainedBy)
        {
            string text = (explanation ?? "").Trim();
            if (text.Length > ExplanationMaxLength) return ExplainOutcome.TooLongRejected;

            using (var cmd = new SqlCommand(
                // An empty value only clears an explanation that is there; on an Unexplained row it is rejected, not a no-op.
                "UPDATE dbo.CostPlusShadowResult SET " +
                "  Explanation = CASE WHEN @text = '' THEN NULL ELSE @text END, " +
                "  ExplainedBy = CASE WHEN @text = '' THEN NULL ELSE @by END, " +
                "  ExplainedAt = CASE WHEN @text = '' THEN NULL ELSE SYSDATETIME() END " +
                "WHERE ShadowResultId = @id AND Diverged = 1 AND (@text <> '' OR Explanation IS NOT NULL)", conn))
            {
                cmd.Parameters.Add("@id", SqlDbType.Int).Value = id;
                cmd.Parameters.Add("@text", SqlDbType.NVarChar, ExplanationMaxLength).Value = text;
                cmd.Parameters.Add("@by", SqlDbType.NVarChar, 256).Value = (object)explainedBy ?? DBNull.Value;
                if (cmd.ExecuteNonQuery() == 1) return text == "" ? ExplainOutcome.Cleared : ExplainOutcome.Saved;
            }

            // Nothing updated: no such diverged comparison, or an empty value on one that has no explanation.
            using (var cmd = new SqlCommand("SELECT COUNT(*) FROM dbo.CostPlusShadowResult WHERE ShadowResultId = @id AND Diverged = 1", conn))
            {
                cmd.Parameters.Add("@id", SqlDbType.Int).Value = id;
                return (int)cmd.ExecuteScalar() == 1 ? ExplainOutcome.EmptyRejected : ExplainOutcome.NotFound;
            }
        }
    }
}
