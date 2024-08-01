using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Linq;
using System.Web;
using System.IO;

namespace AuthenticationServer.Models.Services
{
    public class Log
    {
        public static void LogRequest_Rate(string username, string address, string city, string state, string zip, string country, string request, string response, string direction)
        {
            SqlConnection connLog = new SqlConnection(Configuration.UpsRateSqlConnection);
            connLog.Open();

            SqlCommand cmdLog = new SqlCommand();
            cmdLog.Connection = connLog;
            cmdLog.CommandType = CommandType.StoredProcedure;
            cmdLog.CommandText = "LogRequest_Rate";

            SqlParameter pUserName = new SqlParameter("@UserName", SqlDbType.VarChar, 50);
            SqlParameter pTargetUrl = new SqlParameter("@TargetUrl", SqlDbType.VarChar, 200);
            SqlParameter pAddress = new SqlParameter("@Address", SqlDbType.VarChar, 200);
            SqlParameter pCity = new SqlParameter("@City", SqlDbType.VarChar, 200);
            SqlParameter pState = new SqlParameter("@State", SqlDbType.VarChar, 50);
            SqlParameter pZip = new SqlParameter("@Zip", SqlDbType.VarChar, 50);
            SqlParameter pCountry = new SqlParameter("@Country", SqlDbType.VarChar, 2);
            SqlParameter pRequestXml = new SqlParameter("@RequestXml", SqlDbType.NText);
            SqlParameter pResponseXml = new SqlParameter("@ResponseXml", SqlDbType.NText);
            SqlParameter pRequestId = new SqlParameter("@RequestId", SqlDbType.Int);

            pUserName.Value = username;
            pTargetUrl.Value = Configuration.UPSShopRatesURL;
            pAddress.Value = address;
            pCity.Value = city;
            pState.Value = state;
            pZip.Value = zip;
            pCountry.Value = country;
            pRequestXml.Value = request;
            pResponseXml.Value = response;
            pRequestId.Direction = ParameterDirection.Output;

            cmdLog.Parameters.Add(pUserName);
            cmdLog.Parameters.Add(pTargetUrl);
            cmdLog.Parameters.Add(pAddress);
            cmdLog.Parameters.Add(pCity);
            cmdLog.Parameters.Add(pState);
            cmdLog.Parameters.Add(pZip);
            cmdLog.Parameters.Add(pCountry);
            cmdLog.Parameters.Add(pRequestXml);
            cmdLog.Parameters.Add(pResponseXml);
            cmdLog.Parameters.Add(pRequestId);

            cmdLog.ExecuteNonQuery();

            //RateRequestId = Convert.ToInt32(pRequestId.Value);

            connLog.Close();
        }

        
    }
}