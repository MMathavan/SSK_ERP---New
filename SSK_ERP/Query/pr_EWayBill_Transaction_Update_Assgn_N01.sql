CREATE PROCEDURE [dbo].[pr_EWayBill_Transaction_Update_Assgn_N01]
    @PTranMID INT,
    @PEwayBillNo VARCHAR(50) = NULL,
    @PEwayBillDate DATETIME = NULL,
    @PInfoDetails VARCHAR(MAX) = NULL,
    @PEwayBillValidTill DATETIME = NULL,
    @PEwayBillQrCodeUrl VARCHAR(MAX) = NULL,
    @PEwayBillPdfUrl VARCHAR(MAX) = NULL,
    @PCUSRID VARCHAR(100) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    UPDATE dbo.TRANSACTIONMASTER
    SET
        EWAYBILLNO = @PEwayBillNo,
        EWAYBILLDATE = @PEwayBillDate,
        INFODETAILS = @PInfoDetails,
        EWAYBILLVALIDTILL = @PEwayBillValidTill,
        EWAYBILLQRCODEURL = @PEwayBillQrCodeUrl,
        EWAYBILLPDFURL = @PEwayBillPdfUrl
    WHERE TRANMID = @PTranMID;
END

