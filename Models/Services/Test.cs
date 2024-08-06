using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Web;

namespace AuthenticationServer.Models.Services
{
    public class Test
    {
        public void CheckQueue()
        {
            while (1 == 1)
            {
                processNextRequest();
                Console.ReadKey();
            }
        }

        public static void processNextRequest() {

            SqlConnection conn = new SqlConnection(Configuration.UpsRateSqlConnection);
            conn.Open();

            SqlCommand cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO [dbo].[Test_Paul]  ([id],[testComment]) VALUES ()";

            conn.Close();
        }
    }
}