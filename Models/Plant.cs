using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Linq;
using System.Web;

namespace AuthenticationServer.Models
{
    public class Plant
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string State { get; set; }
        public string Zip {  get; set; }
        public string PostalCode { get; set; }
        public string Country { get; set; }

        public Plant() { }        

        public Plant(string PlantCode)
        {
            SqlConnection conn = new SqlConnection(Configuration.UpsRateSqlConnection);
            conn.Open();

            SqlCommand cmd = new SqlCommand();
            cmd.Connection = conn;
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = "SELECT Address, City, State, Zip, Country FROM Plants WHERE PlantCode = '" + PlantCode + "'";

            SqlDataReader drResults = cmd.ExecuteReader();

            if (drResults.Read())
            {
                this.Address = drResults["Address"].ToString();
                this.City = drResults["City"].ToString();
                this.State = drResults["State"].ToString();
                this.Zip = drResults["Zip"].ToString();
                this.Country = drResults["Country"].ToString();
            }

            conn.Close();
        }

        public static List<Plant> Plants()
        {
            List<Plant> plants = new List<Plant>();
            

            SqlConnection conn = new SqlConnection(Configuration.UpsRateSqlConnection);
            conn.Open();

            SqlCommand cmd = new SqlCommand();
            cmd.Connection = conn;
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = "SELECT DISTINCT PlantCode, Address, City, State, Zip, Country FROM Plants";

            SqlDataReader drResults = cmd.ExecuteReader();

            while (drResults.Read())
            {
                Plant plant = new Plant();
                plant.Id = drResults["PlantCode"].ToString();
                plant.Address = drResults["Address"].ToString();
                plant.City = drResults["City"].ToString();
                plant.State = drResults["State"].ToString();
                plant.Zip = drResults["Zip"].ToString();
                plant.Country = drResults["Country"].ToString();
                plants.Add( plant );
            }

            conn.Close();
            return plants;
        }
    }
}