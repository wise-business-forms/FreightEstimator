using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace AuthenticationServer.Models
{
    public class PlantCharges
    {
        public string PlantId { get; set; }
        public double PerPackageCharge { get; set; }
        public double PerShipmentCharge { get; set; }
        public double NextDayAir {  get; set; }
        public double SecondDayAir { get; set; }
        public double Ground { get; set; }
        public double ThreeDaySelect { get; set; }
        public double NextDayAirSaver { get; set; }
        public double NextDayAirEarlyAM { get; set; }
        public double SecondDayAirAM {  get; set; }
        public double Saver {  get; set; }
    }
}