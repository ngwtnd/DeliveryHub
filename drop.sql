DECLARE @ConstraintName nvarchar(200);
SELECT @ConstraintName = Name FROM sys.default_constraints WHERE parent_object_id = OBJECT_ID('DeliveryServices') AND parent_column_id = (SELECT column_id FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryServices') AND name = 'ServiceType');
IF @ConstraintName IS NOT NULL EXEC('ALTER TABLE DeliveryServices DROP CONSTRAINT ' + @ConstraintName);
IF COL_LENGTH('DeliveryServices', 'ServiceType') IS NOT NULL ALTER TABLE DeliveryServices DROP COLUMN ServiceType;

IF COL_LENGTH('Orders', 'ServiceId') IS NOT NULL BEGIN
    ALTER TABLE Orders DROP CONSTRAINT IF EXISTS FK_Orders_DeliveryServices_ServiceId;
    DROP INDEX IF EXISTS IX_Orders_ServiceId ON Orders;
    ALTER TABLE Orders DROP COLUMN ServiceId;
END

IF COL_LENGTH('Orders', 'ShipperIncome') IS NOT NULL ALTER TABLE Orders DROP COLUMN ShipperIncome;
