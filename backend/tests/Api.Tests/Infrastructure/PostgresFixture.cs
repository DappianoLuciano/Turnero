using Api.Shared.Persistence;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Api.Tests.Infrastructure;

/// <summary>Integration tests that need Postgres join this collection (one container per run).</summary>
[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}

/// <summary>
/// One real Postgres 17 container for the whole test run. Reproduces production setup:
/// a migrator role that owns the schema and a runtime role (no ownership, NOBYPASSRLS) used by
/// the app. Migrations are applied with the migrator role, exactly like in real deployments.
/// Tests isolate their data by creating their own businesses (unique ids/slugs).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Database = "turnero_test";
    private const string MigratorPassword = "migrator-test-pw";
    private const string RuntimePassword = "runtime-test-pw";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("postgres")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string SuperuserConnectionString { get; private set; } = string.Empty;
    public string MigratorConnectionString { get; private set; } = string.Empty;
    public string RuntimeConnectionString { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        var admin = new NpgsqlConnectionStringBuilder(_container.GetConnectionString());
        SuperuserConnectionString = admin.ConnectionString;

        // One statement per command: CREATE DATABASE cannot run inside a multi-statement batch.
        await ExecuteAsync(SuperuserConnectionString,
            $"CREATE ROLE {DatabaseRoles.Migrator} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS PASSWORD '{MigratorPassword}'");
        await ExecuteAsync(SuperuserConnectionString,
            $"CREATE ROLE {DatabaseRoles.Runtime} LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS PASSWORD '{RuntimePassword}'");
        await ExecuteAsync(SuperuserConnectionString, $"CREATE DATABASE {Database} OWNER {DatabaseRoles.Migrator}");

        var appDbAdmin = new NpgsqlConnectionStringBuilder(SuperuserConnectionString) { Database = Database };
        await ExecuteAsync(appDbAdmin.ConnectionString, $"""
            ALTER SCHEMA public OWNER TO {DatabaseRoles.Migrator};
            REVOKE ALL ON SCHEMA public FROM PUBLIC;
            GRANT CONNECT ON DATABASE {Database} TO {DatabaseRoles.Runtime};
            """);
        SuperuserConnectionString = appDbAdmin.ConnectionString;

        MigratorConnectionString = new NpgsqlConnectionStringBuilder(appDbAdmin.ConnectionString)
        {
            Username = DatabaseRoles.Migrator,
            Password = MigratorPassword,
        }.ConnectionString;

        RuntimeConnectionString = new NpgsqlConnectionStringBuilder(appDbAdmin.ConnectionString)
        {
            Username = DatabaseRoles.Runtime,
            Password = RuntimePassword,
        }.ConnectionString;

        await DatabaseMigrator.MigrateAsync(MigratorConnectionString);
        Api = new ApiFactory(this);
    }

    /// <summary>The API under test (connects as the runtime role).</summary>
    public ApiFactory Api { get; private set; } = null!;

    public async ValueTask DisposeAsync()
    {
        if (Api is not null)
        {
            await Api.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
