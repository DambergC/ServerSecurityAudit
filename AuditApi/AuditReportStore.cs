using Microsoft.Data.SqlClient;
using System.Data;

public sealed class AuditReportStore(string connectionString)
{
    public async Task CheckConnectionAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    public async Task InsertAsync(
        Guid id,
        DateTime receivedAtUtc,
        int riskScore,
        string riskLevel,
        string reportJson,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO dbo.AuditReports (Id, ReceivedAtUtc, RiskScore, RiskLevel, ReportJson)
            VALUES (@Id, @ReceivedAtUtc, @RiskScore, @RiskLevel, @ReportJson);
            """;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
        command.Parameters.Add(new SqlParameter("@ReceivedAtUtc", SqlDbType.DateTime2)
        {
            Scale = 7,
            Value = receivedAtUtc
        });
        command.Parameters.Add("@RiskScore", SqlDbType.TinyInt).Value = riskScore;
        command.Parameters.Add("@RiskLevel", SqlDbType.Char, 1).Value = riskLevel;
        command.Parameters.Add("@ReportJson", SqlDbType.NVarChar, -1).Value = reportJson;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        const string sql = "SELECT ReportJson FROM dbo.AuditReports WHERE Id = @Id;";

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }
}
