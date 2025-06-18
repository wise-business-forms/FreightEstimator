using AuthenticationServer.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Data.SqlClient;
using System.Text;
using System.Web.Mvc;

namespace AuthenticationServer.Controllers
{
    public class ReportsController : Controller
    {
        // GET: Reports
        public ActionResult Index()
        {
            return View();
        }

        // GET: Reports
        public ActionResult MarginReport()
        {
            List<Invoice> invoices = new List<Invoice>();

            try
            {
                //SqlConnection connection = new SqlConnection("Server=ALPERP;Initial Catalog=PSLWise;uid=SQLAdmin;pwd=S#cur1ty1sTh#B#st!;");
                SqlConnection connection = new SqlConnection("Server=AZUREDB01\\AZUREDB01;Initial Catalog=DataWarehouse;uid=sa;pwd=95Montana!!!;");
                SqlCommand cmd = connection.CreateCommand();
                StringBuilder stringBuilder = new StringBuilder("SELECT at2.SVIA_S, at2.AGNDT_S, '30', at2.BILLTO, at2.MALPHA_S , at2.INVNO, at2.FRGHT, sum(NetDue) AS NetDue ");
                stringBuilder.Append("FROM Freight_Margin_VIEW AS fm ");
                stringBuilder.Append("INNER JOIN ALPERP.PSLWise.dbo.ARSAL_T1 AS at2 ON ");
                stringBuilder.Append("at2.INVNO = fm.InvoiceNumber ");
                stringBuilder.Append("GROUP BY at2.SVIA_S, at2.AGNDT_S, at2.BILLTO, at2.MALPHA_S , at2.INVNO, at2.FRGHT");
                //stringBuilder.Append("WHERE at2.INVNO = '247290';");
                cmd.CommandText = stringBuilder.ToString();
                connection.Open();

                DbDataReader reader = cmd.ExecuteReader();
                DataTable dataTable = new DataTable();
                dataTable.Load(reader);

                reader.Close();
                connection.Close();

                foreach (DataRow row in dataTable.Rows)
                {
                    Invoice invoice = new Invoice();
                    invoice.Plant = new Plant("ALP");
                    invoice.Date = DateTime.Parse(row["AGNDT_S"].ToString());
                    invoice.SoldCustomerNumber = int.Parse(row["BILLTO"].ToString());
                    invoice.SoldCustomerName = row["MALPHA_S"].ToString();
                    invoice.InvoiceNumber = int.Parse(row["INVNO"].ToString());
                    invoice.AirBillProNumber = "-";
                    invoice.InvoiceFreightBilled = row["FRGHT"].ToString();
                    invoice.TotalCost = row["NETDUE"].ToString(); ; // SUM GroundCommercial, FuelSurcharge, and all accessorials.
                    invoice.FreightMargin = decimal.Parse(row["FRGHT"].ToString()) - decimal.Parse(row["NETDUE"].ToString()); // InvoiceFreightBilled - TotalCost
                    invoice.FreightMarkupPercentage = 0; // FreightMargin / TotalCost
                    invoice.GroundCommercial = row["NETDUE"].ToString();
                    invoice.FuelSurcharge = 0;
                    invoice.Accessorial1 = 0;
                    invoice.Accessorial2 = 0;
                    invoice.Accessorial3 = 0;
                    
                    invoices.Add(invoice);
                }
            }
            catch (Exception ex)
            {
                throw; // Propagate the exception for the caller to handle.
            }

            //try
            //{
            //    SqlConnection connection = new SqlConnection("Server=AZUREDB01\\AZUREDB01;Initial Catalog=DataWarehouse;uid=sa;pwd=95Montana!!!;");
            //    SqlCommand cmd = connection.CreateCommand();
            //    StringBuilder stringBuilder = new StringBuilder("SELECT CustomerRef, NetDue FROM Freight_Margin");
            //    cmd.CommandText = stringBuilder.ToString();
            //    connection.Open();

            //    DbDataReader reader = cmd.ExecuteReader();
            //    DataTable dataTable = new DataTable();
            //    dataTable.Load(reader);

            //    reader.Close();
            //    connection.Close();

            //    foreach (DataRow row in dataTable.Rows)
            //    {
            //        Invoice invoice = new Invoice();
            //        invoice.Plant = new Plant("ALP");
            //        invoice.Date = DateTime.Parse(row["AGNDT_S"].ToString());
            //        invoice.SoldCustomerNumber = int.Parse(row["BILLTO"].ToString());
            //        invoice.SoldCustomerName = row["MALPHA_S"].ToString();
            //        invoice.InvoiceNumber = int.Parse(row["INVNO"].ToString());
            //        invoice.AirBillProNumber = "-";
            //        invoice.InvoiceFreightBilled = 0;
            //        invoice.TotalCost = 0; // SUM GroundCommercial, FuelSurcharge, and all accessorials.
            //        invoice.FreightMargin = 0; // InvoiceFreightBilled - TotalCost
            //        invoice.FreightMarkupPercentage = 0; // FreightMargin / TotalCost
            //        invoice.GroundCommercial = 0;
            //        invoice.FuelSurcharge = 0;
            //        invoice.Accessorial1 = 0;
            //        invoice.Accessorial2 = 0;
            //        invoice.Accessorial3 = 0;

            //        invoices.Add(invoice);
            //    }
            //}
            //catch (Exception ex)
            //{
            //    throw; // Propagate the exception for the caller to handle.
            //}

            return View(invoices);

            //return View();
        }
    }
}