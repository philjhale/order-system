using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace OrderSystem.OrderService.DbMigration;

/// <summary>
/// Runs `CREATE USER ... FROM EXTERNAL PROVIDER` for this service's managed identity, using the
/// CI OIDC identity's short-lived access token (the SQL AAD admin) rather than a resolved
/// managed identity — this is the one step in the system that must authenticate as the CI
/// principal, since the identity being created can't yet authenticate as itself.
/// </summary>
public sealed class SqlContainedUserProvisioner(IOptions<SqlMigrationOptions> options) : ISqlContainedUserProvisioner
{
    public async Task EnsureContainedUserAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;

        await using var connection = new SqlConnection(
            $"Server=tcp:{opts.ServerFqdn},1433;Database={opts.DatabaseName};Encrypt=True;");
        connection.AccessToken = opts.CiAccessToken;
        await connection.OpenAsync(cancellationToken);

        // CREATE USER can't take a parameterized identifier, so the (trusted, Terraform-supplied,
        // not user input) identity name is bracket-escaped and interpolated instead.
        var escapedName = opts.ManagedIdentityName.Replace("]", "]]");

        // ALTER ROLE ... ADD MEMBER is itself idempotent (no error if already a member), so only
        // the CREATE USER needs the explicit existence check.
        var sql = $"""
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @identityName)
            BEGIN
                CREATE USER [{escapedName}] FROM EXTERNAL PROVIDER;
            END
            ALTER ROLE db_datareader ADD MEMBER [{escapedName}];
            ALTER ROLE db_datawriter ADD MEMBER [{escapedName}];
            ALTER ROLE db_ddladmin ADD MEMBER [{escapedName}];
            """;

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@identityName", opts.ManagedIdentityName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
