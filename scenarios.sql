-- Run setup.sql first. Edit @Scenario and run this entire script for each case.
-- Every execution resets ONLY the two target sample tables from the source.
-- Source data remains unchanged. Do not use this script against production.
USE [MigrationVerifierTarget];
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
DECLARE @Scenario int = 1; -- 1 = equal, 2 = missing, 3 = changed, 4 = extra

IF @Scenario NOT BETWEEN 1 AND 4 OR @Scenario IS NULL
BEGIN
    THROW 51001, 'Choose @Scenario 1, 2, 3 or 4.', 1;
END;

BEGIN TRY
    BEGIN TRANSACTION;

    DELETE FROM [dbo].[CustomerContacts];
    DELETE FROM [dbo].[Customers];
    INSERT INTO [dbo].[Customers] ([CustomerId], [DisplayName], [NotesText], [BalanceAmount])
    SELECT [CustomerId], [DisplayName], [NotesText], [BalanceAmount]
    FROM [MigrationVerifierSource].[dbo].[Customers];
    INSERT INTO [dbo].[CustomerContacts] ([AccountId], [ContactId], [ContactLabel], [DetailText])
    SELECT [AccountId], [ContactId], [ContactLabel], [DetailText]
    FROM [MigrationVerifierSource].[dbo].[CustomerContacts];

    IF @Scenario = 2
        DELETE FROM [dbo].[CustomerContacts]
        WHERE [AccountId] = 10 AND [ContactId] = 2;

    IF @Scenario = 3
        -- Only change NULL to empty text. A hash serializer must detect this.
        UPDATE [dbo].[Customers] SET [NotesText] = N''
        WHERE [CustomerId] = 2;

    IF @Scenario = 4
        INSERT INTO [dbo].[CustomerContacts]
            ([AccountId], [ContactId], [ContactLabel], [DetailText])
        VALUES (10, 99, N'Unexpected contact', NULL);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;

SELECT @Scenario AS [ScenarioNumber],
    CASE @Scenario
        WHEN 1 THEN N'Exit 0: Customers 3/3, CustomerContacts 3/3; no differences.'
        WHEN 2 THEN N'Exit 1: CustomerContacts source 3 / target 2; missing key {AccountId:10, ContactId:2}; other categories zero.'
        WHEN 3 THEN N'Exit 1: Customers source 3 / target 3; changed key {CustomerId:2}, NotesText NULL -> empty; other categories zero.'
        WHEN 4 THEN N'Exit 1: CustomerContacts source 3 / target 4; extra key {AccountId:10, ContactId:99}; other categories zero.'
    END AS [ExpectedResult];

-- After EACH execution, run the verifier before selecting the next case:
-- dotnet run -c Release --no-build -- dbo.Customers dbo.CustomerContacts
-- The JSON key display in Markdown uses string values, e.g. {"CustomerId":"2"}.
