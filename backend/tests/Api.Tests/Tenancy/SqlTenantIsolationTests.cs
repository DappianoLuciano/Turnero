using Api.Shared.Persistence;
using Api.Tests.Infrastructure;
using Npgsql;

namespace Api.Tests.Tenancy;

/// <summary>
/// Same guarantees with plain SQL as the runtime role, i.e. without any application code:
/// RLS alone must isolate tenants.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SqlTenantIsolationTests(PostgresFixture db) : IAsyncLifetime
{
    private TwoTenants _t = null!;

    public async ValueTask InitializeAsync() => _t = await TwoTenants.CreateAsync(db);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Select_never_returns_rows_of_other_tenant()
    {
        await AsTenantAsync(_t.A.BusinessId, null, async run =>
        {
            (await run.ScalarAsync("SELECT count(*) FROM business_memberships WHERE business_id = @b", _t.B.BusinessId)).ShouldBe(0);
            (await run.ScalarAsync("SELECT count(*) FROM businesses WHERE id = @b", _t.B.BusinessId)).ShouldBe(0);
            (await run.ScalarAsync("SELECT count(*) FROM business_memberships")).ShouldBe(2);
            (await run.ScalarAsync("SELECT count(*) FROM businesses")).ShouldBe(1);
        });
    }

    [Fact]
    public async Task Update_and_delete_of_other_tenant_affect_zero_rows()
    {
        await AsTenantAsync(_t.A.BusinessId, null, async run =>
        {
            (await run.ExecuteAsync("UPDATE business_memberships SET role = 'Reception' WHERE business_id = @b", _t.B.BusinessId)).ShouldBe(0);
            (await run.ExecuteAsync("DELETE FROM business_memberships WHERE business_id = @b", _t.B.BusinessId)).ShouldBe(0);
            (await run.ExecuteAsync("UPDATE businesses SET name = 'hacked' WHERE id = @b", _t.B.BusinessId)).ShouldBe(0);
            (await run.ExecuteAsync("DELETE FROM businesses WHERE id = @b", _t.B.BusinessId)).ShouldBe(0);
        }, commit: true);

        await AsTenantAsync(_t.B.BusinessId, null, async run =>
        {
            (await run.ScalarAsync("SELECT count(*) FROM business_memberships WHERE role <> 'Reception'")).ShouldBe(2);
            (await run.ScalarAsync("SELECT count(*) FROM businesses WHERE name = 'Business B'")).ShouldBe(1);
        });
    }

    [Fact]
    public async Task Insert_for_other_tenant_violates_policy()
    {
        var ex = await Should.ThrowAsync<PostgresException>(() => AsTenantAsync(_t.A.BusinessId, null, run =>
            run.ExecuteAsync("""
                INSERT INTO business_memberships (id, business_id, user_id, role, created_at)
                VALUES (gen_random_uuid(), @b, @u, 'Owner', now())
                """, _t.B.BusinessId, _t.A.OwnerId)));
        ex.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Moving_own_row_to_other_tenant_violates_policy()
    {
        var ex = await Should.ThrowAsync<PostgresException>(() => AsTenantAsync(_t.A.BusinessId, null, run =>
            run.ExecuteAsync("UPDATE business_memberships SET business_id = @b", _t.B.BusinessId)));
        ex.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Without_tenant_setting_nothing_is_visible()
    {
        await AsTenantAsync(null, null, async run =>
        {
            (await run.ScalarAsync("SELECT count(*) FROM business_memberships")).ShouldBe(0);
            (await run.ScalarAsync("SELECT count(*) FROM businesses")).ShouldBe(0);
        });
    }

    [Fact]
    public async Task Runtime_role_cannot_disable_row_security()
    {
        var ex = await Should.ThrowAsync<PostgresException>(() => AsTenantAsync(_t.A.BusinessId, null, async run =>
        {
            await run.ExecuteAsync("SET LOCAL row_security = off");
            await run.ScalarAsync("SELECT count(*) FROM business_memberships");
        }));
        ex.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Member_self_read_exposes_only_own_memberships_and_is_read_only()
    {
        await AsTenantAsync(null, _t.B.OwnerId, async run =>
        {
            (await run.ScalarAsync("SELECT count(*) FROM business_memberships")).ShouldBe(1);
            (await run.ScalarAsync("SELECT count(*) FROM business_memberships WHERE user_id = @u", _t.B.OwnerId)).ShouldBe(1);
            (await run.ExecuteAsync("UPDATE business_memberships SET role = 'Staff' WHERE user_id = @u", _t.B.OwnerId)).ShouldBe(0);
            (await run.ScalarAsync("SELECT count(*) FROM businesses")).ShouldBe(0);
        });
    }

    [Fact]
    public async Task Tenant_setting_does_not_leak_after_transaction_ends()
    {
        await using var connection = new NpgsqlConnection(db.RuntimeConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using (var tx = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            await new SqlRunner(connection, tx).SetTenantAsync(_t.A.BusinessId, null);
            await tx.CommitAsync(TestContext.Current.CancellationToken);
        }

        var runner = new SqlRunner(connection, null);
        (await runner.ScalarAsync("SELECT count(*) FROM business_memberships")).ShouldBe(0);
    }

    private async Task AsTenantAsync(Guid? businessId, Guid? userId, Func<SqlRunner, Task> work, bool commit = false)
    {
        await using var connection = new NpgsqlConnection(db.RuntimeConnectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        var runner = new SqlRunner(connection, tx);
        await runner.SetTenantAsync(businessId, userId);
        await work(runner);
        if (commit)
        {
            await tx.CommitAsync();
        }
    }

    private sealed class SqlRunner(NpgsqlConnection connection, NpgsqlTransaction? transaction)
    {
        public Task SetTenantAsync(Guid? businessId, Guid? userId) => ExecuteRawAsync(
            $"SELECT set_config('{TenantSettings.BusinessId}', @p0, true), set_config('{TenantSettings.UserId}', @p1, true)",
            businessId?.ToString() ?? string.Empty, userId?.ToString() ?? string.Empty);

        public async Task<long> ScalarAsync(string sql, params object[] args)
        {
            await using var command = Build(Bind(sql), args);
            return Convert.ToInt64(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
        }

        public async Task<int> ExecuteAsync(string sql, params object[] args)
        {
            await using var command = Build(Bind(sql), args);
            return await command.ExecuteNonQueryAsync();
        }

        private async Task ExecuteRawAsync(string sql, params object[] args)
        {
            await using var command = Build(sql, args);
            await command.ExecuteNonQueryAsync();
        }

        /// <summary>Maps the readable @b / @u placeholders to positional parameters in order of appearance.</summary>
        private static string Bind(string sql)
        {
            var index = 0;
            return System.Text.RegularExpressions.Regex.Replace(sql, "@[bu]\\b", _ => $"@p{index++}");
        }

        private NpgsqlCommand Build(string sql, object[] args)
        {
            var command = new NpgsqlCommand(sql, connection, transaction);
            for (var i = 0; i < args.Length; i++)
            {
                command.Parameters.AddWithValue($"p{i}", args[i]);
            }

            return command;
        }
    }
}
