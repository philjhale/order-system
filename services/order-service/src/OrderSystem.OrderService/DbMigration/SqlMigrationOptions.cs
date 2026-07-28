namespace OrderSystem.OrderService.DbMigration;

/// <summary>
/// Bound from the "Sql" configuration section, populated only for the `migrate` CLI mode's
/// container app job (services/order-service/infra/terraform) — the normal running Container App never sets these.
/// </summary>
public sealed class SqlMigrationOptions
{
    public string ServerFqdn { get; set; } = "";
    public string DatabaseName { get; set; } = "";

    /// <summary>Must match the exact display name of this service's managed identity in Azure AD.</summary>
    public string ManagedIdentityName { get; set; } = "";

    /// <summary>
    /// Short-lived token for https://database.windows.net, fetched by the CI workflow step
    /// (already authenticated as the CI OIDC identity, the SQL AAD admin) and passed into the
    /// container app job as a one-shot env var. Used only for the CREATE USER connection —
    /// every other connection in this service authenticates as its own managed identity.
    /// </summary>
    public string CiAccessToken { get; set; } = "";
}
