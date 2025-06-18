using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace AuthenticationServer.Models
{
	public class Invoice
	{
		public int InvoiceNumber { get; set; }
		public DateTime Date { get; set; }
		public Plant Plant { get; set; }
		public int SoldCustomerNumber { get; set; }
        public string SoldCustomerName { get; set; }
		public string AirBillProNumber { get; set; }
		public string InvoiceFreightBilled { get; set; }
        public string TotalCost { get; set; }
        public decimal FreightMargin { get; set; }
        public float FreightMarkupPercentage { get; set; }
        public string GroundCommercial { get; set; }
        public float FuelSurcharge { get; set; }
        public float Accessorial1 { get; set; }
        public float Accessorial2 { get; set; }
        public float Accessorial3 { get; set; }
    }
}