CREATE TABLE [dbo].[transactionpinvoicemaster](
    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [UploadBatchId] UNIQUEIDENTIFIER NOT NULL,
    [OriginalPdfFileName] NVARCHAR(255) NOT NULL,
    [UploadedOn] DATETIME NOT NULL DEFAULT(GETDATE()),
    [UploadedBy] NVARCHAR(100) NULL,
    [SupplierId] INT NULL,
    [PurchaseOrderId] INT NULL,
    [PoNumber] NVARCHAR(50) NULL,
    [PoDate] DATETIME NULL,
    [CustomerInfo] NVARCHAR(MAX) NULL,
    [FullExtractedText] NVARCHAR(MAX) NULL
);

CREATE TABLE [dbo].[transactionpinvoicedetail](
    [Id] INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    [TransactionPInvoiceMasterId] INT NOT NULL,
    [LineNo] INT NOT NULL,
    [MfgCode] NVARCHAR(50) NULL,
    [Cat] NVARCHAR(10) NULL,
    [ProductDescription] NVARCHAR(MAX) NULL,
    [HsnCode] NVARCHAR(50) NULL,
    [UnitUom] NVARCHAR(20) NULL,
    [BatchNo] NVARCHAR(50) NULL,
    [ExpiryText] NVARCHAR(20) NULL,
    [Boxes] NVARCHAR(20) NULL,
    [TotalQty] DECIMAL(18,2) NULL,
    [PricePerUnit] DECIMAL(18,2) NULL,
    [Ptr] DECIMAL(18,2) NULL,
    [Mrp] DECIMAL(18,2) NULL,
    [TotalValue] DECIMAL(18,2) NULL,
    [DiscPercent] DECIMAL(18,2) NULL,
    [DiscountValue] DECIMAL(18,2) NULL,
    [TaxableValue] DECIMAL(18,2) NULL,
    [CgstRate] DECIMAL(18,2) NULL,
    [CgstAmount] DECIMAL(18,2) NULL,
    [SgstRate] DECIMAL(18,2) NULL,
    [SgstAmount] DECIMAL(18,2) NULL,
    [TotalAmount] DECIMAL(18,2) NULL,
    [RawLineText] NVARCHAR(MAX) NULL,
    CONSTRAINT [FK_transactionpinvoicedetail_master] FOREIGN KEY([TransactionPInvoiceMasterId])
        REFERENCES [dbo].[transactionpinvoicemaster]([Id])
);
