-- Run once in SSMS or sqlcmd as a login authorized to create databases.
-- GO is a client batch separator. Existing databases are never dropped.
USE [master];
GO
IF DB_ID(N'MigrationVerifierSource') IS NOT NULL
    OR DB_ID(N'MigrationVerifierTarget') IS NOT NULL
BEGIN
    THROW 51000, 'Sample database names already exist. Choose unused names before running setup.', 1;
END;
EXEC(N'CREATE DATABASE [MigrationVerifierSource];');
EXEC(N'CREATE DATABASE [MigrationVerifierTarget];');
EXEC(N'ALTER DATABASE [MigrationVerifierSource] SET ALLOW_SNAPSHOT_ISOLATION ON;');
EXEC(N'ALTER DATABASE [MigrationVerifierTarget] SET ALLOW_SNAPSHOT_ISOLATION ON;');
GO
USE [MigrationVerifierSource];
GO
CREATE TABLE [dbo].[Customers]
(
    [CustomerId] int NOT NULL,
    [DisplayName] nvarchar(100) NOT NULL,
    [NotesText] nvarchar(200) NULL,
    [BalanceAmount] decimal(18,2) NOT NULL,
    CONSTRAINT [PK_Customers] PRIMARY KEY ([CustomerId])
);
CREATE TABLE [dbo].[CustomerContacts]
(
    [AccountId] int NOT NULL,
    [ContactId] int NOT NULL,
    [ContactLabel] nvarchar(100) NOT NULL,
    [DetailText] nvarchar(200) NULL,
    CONSTRAINT [PK_CustomerContacts] PRIMARY KEY ([AccountId], [ContactId])
);
INSERT INTO [dbo].[Customers] ([CustomerId], [DisplayName], [NotesText], [BalanceAmount])
VALUES (1, N'Alpha', N'Ready', 12.50),
       (2, N'Beta', NULL, 0.00),
       (3, N'Gamma', N'', 99.95);
INSERT INTO [dbo].[CustomerContacts] ([AccountId], [ContactId], [ContactLabel], [DetailText])
VALUES (10, 1, N'Primary', N'First contact'),
       (10, 2, N'Secondary', NULL),
       (20, 1, N'International', N'España — 日本');
GO
USE [MigrationVerifierTarget];
GO
CREATE TABLE [dbo].[Customers]
(
    [CustomerId] int NOT NULL,
    [DisplayName] nvarchar(100) NOT NULL,
    [NotesText] nvarchar(200) NULL,
    [BalanceAmount] decimal(18,2) NOT NULL,
    CONSTRAINT [PK_Customers] PRIMARY KEY ([CustomerId])
);
CREATE TABLE [dbo].[CustomerContacts]
(
    [AccountId] int NOT NULL,
    [ContactId] int NOT NULL,
    [ContactLabel] nvarchar(100) NOT NULL,
    [DetailText] nvarchar(200) NULL,
    CONSTRAINT [PK_CustomerContacts] PRIMARY KEY ([AccountId], [ContactId])
);
INSERT INTO [dbo].[Customers] ([CustomerId], [DisplayName], [NotesText], [BalanceAmount])
SELECT [CustomerId], [DisplayName], [NotesText], [BalanceAmount]
FROM [MigrationVerifierSource].[dbo].[Customers];
INSERT INTO [dbo].[CustomerContacts] ([AccountId], [ContactId], [ContactLabel], [DetailText])
SELECT [AccountId], [ContactId], [ContactLabel], [DetailText]
FROM [MigrationVerifierSource].[dbo].[CustomerContacts];
GO
-- Initial expected result: both tables match, three rows in each table and database.
