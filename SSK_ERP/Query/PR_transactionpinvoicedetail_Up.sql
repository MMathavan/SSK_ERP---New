USE [SSK_ERP]
GO

SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE PROCEDURE [dbo].[PR_transactionpinvoicedetail_Up]
    @kusrid varchar(25)
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        @kusrid AS KUSRID,
        T.*,
        M.*
        --, T.RatePerUnit + ((T.RatePerUnit * M.MTRLPRFT) / 100) AS Actualrate
    FROM transactionpinvoicedetail T
    LEFT JOIN MATERIALMASTER M
        ON REPLACE(T.[ProductDescription], '-', ' ') COLLATE SQL_Latin1_General_CP1_CI_AS
           LIKE '%' + REPLACE(M.MTRLDESC, '-', ' ') COLLATE SQL_Latin1_General_CP1_CI_AS + '%';
END
GO
