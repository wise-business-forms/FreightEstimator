using System.Configuration;
using System.Data;
using System.Data.SqlClient;

namespace FreightEstimator.QuoteTests
{
    /// <summary>
    /// Puts known Plant Charges into SANDBOX PlantCarrierCharges2017 for one plant and puts the plant's
    /// original rows back afterwards, so no test depends on whatever values SANDBOX happens to hold.
    /// Dispose restores. Refuses to run against anything but SANDBOX (SandboxGuard).
    /// </summary>
    public sealed class SandboxPlantCharges : IDisposable
    {
        private const string Columns = "CarrierPlantChargesId, CarrierId, PlantCode, PerPackageCharge, PerShipmentCharge, NextDayAir, SecondDayAir, Ground, ThreeDaySelect, NextDayAirSaver, NextDayAirEarlyAM, SecondDayAirAM, Saver, Active";

        private readonly string _plant;
        private readonly DataTable _original;

        /// <summary>One carrier's charges for a plant. Markups are percentages.</summary>
        public class Row
        {
            public string CarrierId;
            public decimal PerPackage, PerShipment;
            public decimal NextDayAir, SecondDayAir, Ground, ThreeDaySelect, NextDayAirSaver, NextDayAirEarlyAM, SecondDayAirAM, Saver;
        }

        /// <summary>
        /// The schedule every case runs with. Every number is different on purpose, so a quote that picks
        /// up the wrong service's markup, or the CWT row instead of the non-CWT row, lands on a wrong number.
        /// </summary>
        public static Row[] KnownSchedule() => new[]
        {
            new Row { CarrierId = "UPS",    PerPackage = 2.00m, PerShipment = 5.00m, NextDayAir = 3m, SecondDayAir = 4m, Ground = 5m, ThreeDaySelect = 6m, NextDayAirSaver = 7m, NextDayAirEarlyAM = 8m, SecondDayAirAM = 9m, Saver = 10m },
            new Row { CarrierId = "UPSCWT", PerPackage = 1.00m, PerShipment = 3.00m, NextDayAir = 10m, SecondDayAir = 11m, Ground = 12m, ThreeDaySelect = 13m, NextDayAirSaver = 14m, NextDayAirEarlyAM = 15m, SecondDayAirAM = 16m, Saver = 17m },
            new Row { CarrierId = "GF",     PerPackage = 2.00m, PerShipment = 5.00m, NextDayAir = 0m, SecondDayAir = 0m, Ground = 5m, ThreeDaySelect = 0m, NextDayAirSaver = 0m, NextDayAirEarlyAM = 0m, SecondDayAirAM = 0m, Saver = 0m },
            // Logistics Partner (LTL): the plant's markup % is the Ground column.
            new Row { CarrierId = "TI",     PerPackage = 3.00m, PerShipment = 6.00m, NextDayAir = 0m, SecondDayAir = 0m, Ground = 7.5m, ThreeDaySelect = 0m, NextDayAirSaver = 0m, NextDayAirEarlyAM = 0m, SecondDayAirAM = 0m, Saver = 0m },
        };

        public SandboxPlantCharges(string plant, params Row[] rows)
        {
            SandboxGuard.Require();
            _plant = plant;

            using (var conn = Open())
            {
                _original = new DataTable();
                using (var cmd = new SqlCommand("SELECT " + Columns + " FROM dbo.PlantCarrierCharges2017 WHERE PlantCode = @p", conn))
                {
                    cmd.Parameters.AddWithValue("@p", plant);
                    new SqlDataAdapter(cmd).Fill(_original);
                }

                Exec(conn, "DELETE FROM dbo.PlantCarrierCharges2017 WHERE PlantCode = @p", ("@p", plant));

                int nextId = Convert.ToInt32(Scalar(conn, "SELECT ISNULL(MAX(CarrierPlantChargesId), 0) + 1 FROM dbo.PlantCarrierCharges2017"));
                foreach (Row r in rows)
                {
                    Exec(conn,
                        "INSERT dbo.PlantCarrierCharges2017 (" + Columns + ") VALUES (@id, @c, @p, @pp, @ps, @nda, @sda, @g, @ts, @ndas, @ndam, @sdam, @s, 'Y')",
                        ("@id", nextId++), ("@c", r.CarrierId), ("@p", plant), ("@pp", r.PerPackage), ("@ps", r.PerShipment),
                        ("@nda", r.NextDayAir), ("@sda", r.SecondDayAir), ("@g", r.Ground), ("@ts", r.ThreeDaySelect),
                        ("@ndas", r.NextDayAirSaver), ("@ndam", r.NextDayAirEarlyAM), ("@sdam", r.SecondDayAirAM), ("@s", r.Saver));
                }
            }
        }

        public void Dispose()
        {
            using (var conn = Open())
            {
                Exec(conn, "DELETE FROM dbo.PlantCarrierCharges2017 WHERE PlantCode = @p", ("@p", _plant));
                foreach (DataRow row in _original.Rows)
                {
                    var values = row.ItemArray;
                    using (var cmd = new SqlCommand("INSERT dbo.PlantCarrierCharges2017 (" + Columns + ") VALUES (" +
                        string.Join(", ", Enumerable.Range(0, values.Length).Select(i => "@v" + i)) + ")", conn))
                    {
                        for (int i = 0; i < values.Length; i++) cmd.Parameters.AddWithValue("@v" + i, values[i]);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
        }

        private static SqlConnection Open()
        {
            var conn = new SqlConnection(ConfigurationManager.ConnectionStrings["UpsRate"].ConnectionString);
            conn.Open();
            return conn;
        }

        private static void Exec(SqlConnection conn, string sql, params (string Name, object Value)[] parameters)
        {
            using (var cmd = new SqlCommand(sql, conn))
            {
                foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
                cmd.ExecuteNonQuery();
            }
        }

        private static object Scalar(SqlConnection conn, string sql)
        {
            using (var cmd = new SqlCommand(sql, conn)) return cmd.ExecuteScalar();
        }
    }
}
