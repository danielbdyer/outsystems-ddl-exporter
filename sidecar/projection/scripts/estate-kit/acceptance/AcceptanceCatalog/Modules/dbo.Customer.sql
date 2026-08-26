CREATE TABLE [dbo].[Customer] (
    [Id]       INT            IDENTITY(1,1) NOT NULL,
    [Name]     NVARCHAR(100)  NOT NULL,
    [Email]    NVARCHAR(250)  NULL,
    [StatusId] INT            NOT NULL,
    [RegionId] INT            NULL,
    [Score]    INT            NULL,
    CONSTRAINT [PK_Customer] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Customer_Status] FOREIGN KEY ([StatusId]) REFERENCES [dbo].[Status] ([Id])
);
