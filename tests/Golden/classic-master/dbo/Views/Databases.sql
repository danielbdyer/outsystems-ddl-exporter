-- Resolves only through the master.dacpac reference: without it the build fails with SQL71561 on master.sys.databases.
CREATE VIEW [dbo].[Databases]
AS
SELECT [name], [collation_name] FROM [master].[sys].[databases];
