using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Web;

namespace AuthenticationServer.Models.Data
{
    public class Request
    {
        public void RecordRequest(string UserName, string TargetUrl, string Address, string City, string State, string Zip, string Country, string RequestXML,  string ResponseXML)
        {
            try
            {
                using (SqlConnection connection = new SqlConnection(Configuration.UpsRateSqlConnection))
                {
                    connection.Open();
                    using (SqlCommand command = new SqlCommand("GetEmployeeById", connection))
                    {
                        command.CommandType = System.Data.CommandType.StoredProcedure;

                        SqlParameter _userName = new SqlParameter("@UserName", SqlDbType.VarChar, 50);
                        SqlParameter _targetUrl = new SqlParameter("@TsargetUrl", SqlDbType.VarChar, 200);
                        SqlParameter _address = new SqlParameter("@Address", SqlDbType.VarChar, 200);
                        SqlParameter _city = new SqlParameter("@City", SqlDbType.VarChar, 200);
                        SqlParameter _state = new SqlParameter("@State", SqlDbType.VarChar, 50);
                        SqlParameter _zip = new SqlParameter("@Zip", SqlDbType.VarChar, 50);
                        SqlParameter _country = new SqlParameter("@Country", SqlDbType.VarChar, 2);
                        SqlParameter _RequestXML = new SqlParameter("@RequestXml", SqlDbType.NText);
                        SqlParameter _ResponseXML = new SqlParameter("@ResponseXML", SqlDbType.NText);

                        _userName.Value = UserName;
                        _targetUrl.Value = TargetUrl;
                        _address.Value = Address;
                        _city.Value = City;
                        _state.Value = State;
                        _zip.Value = Zip;
                        _country.Value = Country;
                        _RequestXML.Value = RequestXML;
                        _ResponseXML.Value = ResponseXML;

                        command.ExecuteNonQuery();
                        connection.Close();
                    }

                }
            }
            catch(Exception ex)
            {
                // Throw to proper error handling
            }
        }
    }
}