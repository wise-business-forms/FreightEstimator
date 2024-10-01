using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Data;
using System.Linq;
using System.Web;
using System.Numerics;

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
            cmd.CommandText = "SELECT DISTINCT PlantCode, PlantName, Address, City, State, Zip, Country FROM Plants WHERE Active = 'Y'";

            SqlDataReader drResults = cmd.ExecuteReader();

            while (drResults.Read())
            {
                Plant plant = new Plant();
                plant.Id = drResults["PlantCode"].ToString();
                plant.Name = drResults["PlantName"].ToString();
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

        public static List<PlantCharges> Charges(string plantID)
        {
            List<PlantCharges> charges = new List<PlantCharges>();
            SqlConnection conn = new SqlConnection(Configuration.UpsRateSqlConnection);
            conn.Open();

            SqlCommand cmd = new SqlCommand();
            cmd.Connection = conn;
            cmd.CommandType = CommandType.Text;
            cmd.CommandText = "SELECT * FROM PlantCarrierCharges2017 WHERE PlantCode = '" + plantID + "'";

            SqlDataReader drResults = cmd.ExecuteReader();

            while (drResults.Read())
            {
                PlantCharges plantCharges = new PlantCharges();
                plantCharges.PlantId = drResults["PlantCode"].ToString();
                plantCharges.CarrierId = drResults["CarrierId"].ToString();
                plantCharges.PerPackageCharge = Convert.ToDouble(drResults["PerPackageCharge"].ToString());
                plantCharges.PerShipmentCharge = Convert.ToDouble(drResults["PershipmentCharge"].ToString());
                plantCharges.NextDayAir = Convert.ToDouble(drResults["NextDayAir"].ToString());
                plantCharges.SecondDayAir = Convert.ToDouble(drResults["SecondDayAir"].ToString());
                plantCharges.Ground = Convert.ToDouble(drResults["Ground"].ToString());
                plantCharges.ThreeDaySelect = Convert.ToDouble(drResults["ThreeDaySelect"].ToString());
                plantCharges.NextDayAirSaver = Convert.ToDouble(drResults["NextDayAirSaver"].ToString());
                plantCharges.NextDayAirEarlyAM = Convert.ToDouble(drResults["NextDayAirEarlyAM"].ToString());
                plantCharges.SecondDayAirAM = Convert.ToDouble(drResults["SecondDayairAM"].ToString());
                plantCharges.Saver = Convert.ToDouble(drResults["Saver"].ToString());
                charges.Add(plantCharges);
            }

            conn.Close();

            return charges;
        }
    }
}