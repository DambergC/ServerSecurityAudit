CREATE TABLE dbo.AuditReports
(
    Id uniqueidentifier NOT NULL CONSTRAINT PK_AuditReports PRIMARY KEY,
    ReceivedAtUtc datetime2(7) NOT NULL,
    RiskScore tinyint NOT NULL CONSTRAINT CK_AuditReports_RiskScore CHECK (RiskScore <= 100),
    RiskLevel char(1) NOT NULL CONSTRAINT CK_AuditReports_RiskLevel CHECK (RiskLevel IN ('A', 'B', 'C', 'D', 'F')),
    ReportJson nvarchar(max) NOT NULL CONSTRAINT CK_AuditReports_ReportJson CHECK (ISJSON(ReportJson) = 1)
);

CREATE INDEX IX_AuditReports_ReceivedAtUtc
    ON dbo.AuditReports (ReceivedAtUtc DESC);
