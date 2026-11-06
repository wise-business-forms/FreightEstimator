-- =====================================================================================================
-- FreightEstimator database: seed data for dbo.Plants
--
-- Run after Database/FreightEstimator.sql, in the FreightEstimator database only. Refuses to run anywhere else
-- and does nothing if dbo.Plants already has rows.
--
-- The ten plants and their sites are copied from UPSRate (read only; nothing there was changed). The UPS shipper
-- number is NOT copied: the app only checks that UpsShippingNumber is not NULL, so every active plant gets the
-- placeholder 'SANDBOX', which cannot ship anything. Inactive plants keep NULL, so they stay unquotable.
--
-- dbo.PlantCarrierCharges2017 (the Plant Charges, i.e. pricing) is deliberately not seeded here. The tests set
-- their own known charges per plant and restore the originals afterwards.
-- =====================================================================================================

SET NOCOUNT ON;
GO

IF DB_NAME() <> N'FreightEstimator'
BEGIN
    RAISERROR('Wrong database: this script must run in a database named FreightEstimator. Nothing was changed.', 16, 1);
    SET NOEXEC ON;
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Plants)
BEGIN
    INSERT dbo.Plants (PlantCode, PlantName, Address, City, State, Zip, Country, Active, GLCode, UpsShippingNumber) VALUES ('ALP', 'Alpharetta', '555 McFarland 400 Drive', 'Alpharetta', 'GA', '30004', 'US', 'Y', 30, 'SANDBOX');
    INSERT dbo.Plants (PlantCode, PlantName, Address, City, State, Zip, Country, Active, GLCode, UpsShippingNumber) VALUES ('ALZ', '', '', '', '', '', '  ', 'N', 0, NULL);
    INSERT dbo.Plants (PlantCode, PlantName, Address, City, State, Zip, Country, Active, GLCode, UpsShippingNumber) VALUES ('AND', 'Anderson', '601 Vanguard Road', 'Anderson', 'SC', '29625', 'US', 'Y', 0, 'SANDBOX');
    INSERT dbo.Plants (PlantCode, PlantName, Address, City, State, Zip, Country, Active, GLCode, UpsShippingNumber) VALUES ('BUT', 'Butler', '150 Kriess Road', 'Butler', 'PA', '16001', 'US', 'Y', 10, 'SANDBOX');
    INSERT dbo.Plants (PlantCode, PlantName, Address, City, State, Zip, Country, Active, GLCode, UpsShippingNumber) VALUES ('CIN', 'Cincinnati', '7188 Main Street', 'Cincinnati', 'OH', '45244', 'US', 'N', 0, NULL);
    INSERT dbo.Plants (PlantCode, PlantName, Address, City, State, Zip, Country, Active, GLCode, UpsShippingNumber) VALUES ('COR', 'Corporate', '115 Whiting Farms Road', 'Holyoke', 'MA', '01040', 'US', 'N', 0, NULL);
    INSERT dbo.Plants (PlantCode, PlantName, Address, City, State, Zip, Country, Active, GLCode, UpsShippingNumber) VALUES ('EBT', 'East Butler', '500 Grant Avenue', 'East Butler', 'PA', '16029', 'US', 'Y', 0, 'SANDBOX');
    INSERT dbo.Plants (PlantCode, PlantName, Address, City, State, Zip, Country, Active, GLCode, UpsShippingNumber) VALUES ('FTW', 'Fort Wayne', '4301 Merchant Road', 'Fort Wayne', 'IN', '46818', 'US', 'Y', 20, 'SANDBOX');
    INSERT dbo.Plants (PlantCode, PlantName, Address, City, State, Zip, Country, Active, GLCode, UpsShippingNumber) VALUES ('PDT', 'Piedmont', '118 Hurricane Creek Road', 'Piedmont', 'SC', '29673-8468', 'US', 'N', 0, NULL);
    INSERT dbo.Plants (PlantCode, PlantName, Address, City, State, Zip, Country, Active, GLCode, UpsShippingNumber) VALUES ('POR', 'Portland', '33 McAlister Farm Rd', 'Portland', 'ME', '04103', 'US', 'Y', 60, 'SANDBOX');
END
GO

SET NOEXEC OFF;
GO

SELECT DB_NAME() AS [Database], COUNT(*) AS Plants, SUM(CASE WHEN Active = 'Y' AND UpsShippingNumber IS NOT NULL THEN 1 ELSE 0 END) AS Quotable FROM dbo.Plants;
GO
