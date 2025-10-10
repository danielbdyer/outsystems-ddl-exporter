ALTER TABLE dbo.Customer
    ADD CONSTRAINT FK_Customer_CityId
        FOREIGN KEY (CityId)
            REFERENCES dbo.City (Id)
            ON DELETE NO ACTION
            ON UPDATE NO ACTION
