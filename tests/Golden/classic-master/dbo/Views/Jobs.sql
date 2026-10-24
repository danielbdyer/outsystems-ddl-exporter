-- Resolves only through the msdb.dacpac reference.
CREATE VIEW [dbo].[Jobs]
AS
SELECT [job_id], [name] FROM [msdb].[dbo].[sysjobs];
