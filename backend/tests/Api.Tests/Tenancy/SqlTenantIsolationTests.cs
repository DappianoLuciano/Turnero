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
    public async Task Services_table_is_isolated_with_plain_sql()
    {
        await AsTenantAsync(_t.A.BusinessId, null, run => run.ExecuteAsync(InsertService, _t.A.BusinessId), commit: true);
        await AsTenantAsync(_t.B.BusinessId, null, run => run.ExecuteAsync(InsertService, _t.B.BusinessId), commit: true);

        await AsTenantAsync(_t.A.BusinessId, null, async run =>
        {
            (await run.ScalarAsync("SELECT count(*) FROM services WHERE business_id = @b", _t.B.BusinessId)).ShouldBe(0);
            (await run.ScalarAsync("SELECT count(*) FROM services")).ShouldBe(1);
            (await run.ExecuteAsync("UPDATE services SET name = 'hacked' WHERE business_id = @b", _t.B.BusinessId)).ShouldBe(0);
            (await run.ExecuteAsync("DELETE FROM services WHERE business_id = @b", _t.B.BusinessId)).ShouldBe(0);
        });

        var ex = await Should.ThrowAsync<PostgresException>(() =>
            AsTenantAsync(_t.A.BusinessId, null, run => run.ExecuteAsync(InsertService, _t.B.BusinessId)));
        ex.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);

        await AsTenantAsync(null, null, async run =>
            (await run.ScalarAsync("SELECT count(*) FROM services")).ShouldBe(0));
    }

    [Fact]
    public async Task Customers_table_is_isolated_with_plain_sql()
    {
        await AsTenantAsync(_t.A.BusinessId, null, run => run.ExecuteAsync(InsertCustomer, _t.A.BusinessId), commit: true);
        await AsTenantAsync(_t.B.BusinessId, null, run => run.ExecuteAsync(InsertCustomer, _t.B.BusinessId), commit: true);

        await AsTenantAsync(_t.A.BusinessId, null, async run =>
        {
            (await run.ScalarAsync("SELECT count(*) FROM customers WHERE business_id = @b", _t.B.BusinessId)).ShouldBe(0);
            (await run.ScalarAsync("SELECT count(*) FROM customers")).ShouldBe(1);
            (await run.ExecuteAsync("UPDATE customers SET name = 'hacked' WHERE business_id = @b", _t.B.BusinessId)).ShouldBe(0);
            (await run.ExecuteAsync("DELETE FROM customers WHERE business_id = @b", _t.B.BusinessId)).ShouldBe(0);
        });

        var ex = await Should.ThrowAsync<PostgresException>(() =>
            AsTenantAsync(_t.A.BusinessId, null, run => run.ExecuteAsync(InsertCustomer, _t.B.BusinessId)));
        ex.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);

        await AsTenantAsync(null, null, async run =>
            (await run.ScalarAsync("SELECT count(*) FROM customers")).ShouldBe(0));
    }

    [Fact]
    public async Task Professionals_and_their_services_are_isolated_with_plain_sql()
    {
        await AsTenantAsync(_t.A.BusinessId, null, run => run.ExecuteAsync(InsertProfessionalWithService, _t.A.BusinessId, _t.A.BusinessId, _t.A.BusinessId), commit: true);
        await AsTenantAsync(_t.B.BusinessId, null, run => run.ExecuteAsync(InsertProfessionalWithService, _t.B.BusinessId, _t.B.BusinessId, _t.B.BusinessId), commit: true);

        foreach (var table in new[] { "professionals", "professional_services" })
        {
            await AsTenantAsync(_t.A.BusinessId, null, async run =>
            {
                (await run.ScalarAsync($"SELECT count(*) FROM {table} WHERE business_id = @b", _t.B.BusinessId)).ShouldBe(0);
                (await run.ScalarAsync($"SELECT count(*) FROM {table}")).ShouldBe(1);
                (await run.ExecuteAsync($"DELETE FROM {table} WHERE business_id = @b", _t.B.BusinessId)).ShouldBe(0);
            });

            await AsTenantAsync(null, null, async run =>
                (await run.ScalarAsync($"SELECT count(*) FROM {table}")).ShouldBe(0));
        }

        await AsTenantAsync(_t.A.BusinessId, null, async run =>
            (await run.ExecuteAsync("UPDATE professionals SET name = 'hacked' WHERE business_id = @b", _t.B.BusinessId)).ShouldBe(0));

        var crossProfessional = await Should.ThrowAsync<PostgresException>(() =>
            AsTenantAsync(_t.A.BusinessId, null, run => run.ExecuteAsync(
                "INSERT INTO professionals (id, business_id, name, is_active, created_at, updated_at) "
                + "VALUES (gen_random_uuid(), @b, 'Colado', true, now(), now())", _t.B.BusinessId)));
        crossProfessional.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);

        var crossLink = await Should.ThrowAsync<PostgresException>(() =>
            AsTenantAsync(_t.A.BusinessId, null, run => run.ExecuteAsync(
                "INSERT INTO professional_services (business_id, professional_id, service_id) "
                + "VALUES (@b, gen_random_uuid(), gen_random_uuid())", _t.B.BusinessId)));
        crossLink.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Appointments_table_is_isolated_with_plain_sql()
    {
        var a = _t.A.BusinessId;
        var b = _t.B.BusinessId;
        await AsTenantAsync(a, null, run => run.ExecuteAsync(InsertAppointment, a, a, a, a), commit: true);
        await AsTenantAsync(b, null, run => run.ExecuteAsync(InsertAppointment, b, b, b, b), commit: true);

        await AsTenantAsync(a, null, async run =>
        {
            (await run.ScalarAsync("SELECT count(*) FROM appointments WHERE business_id = @b", b)).ShouldBe(0);
            (await run.ScalarAsync("SELECT count(*) FROM appointments")).ShouldBe(1);
            (await run.ExecuteAsync("UPDATE appointments SET notes = 'hacked' WHERE business_id = @b", b)).ShouldBe(0);
            (await run.ExecuteAsync("DELETE FROM appointments WHERE business_id = @b", b)).ShouldBe(0);
        });

        var ex = await Should.ThrowAsync<PostgresException>(() =>
            AsTenantAsync(a, null, run => run.ExecuteAsync(
                "INSERT INTO appointments (id, business_id, customer_id, professional_id, service_id, starts_at, ends_at, "
                + "status, price, currency, created_at, updated_at) VALUES (gen_random_uuid(), @b, gen_random_uuid(), "
                + "gen_random_uuid(), gen_random_uuid(), now(), now() + interval '30 minutes', 'Scheduled', 100, 'ARS', now(), now())",
                b)));
        ex.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);

        await AsTenantAsync(null, null, async run =>
            (await run.ScalarAsync("SELECT count(*) FROM appointments")).ShouldBe(0));
    }

    [Fact]
    public async Task Payments_table_is_isolated_and_append_only_with_plain_sql()
    {
        var a = _t.A.BusinessId;
        var b = _t.B.BusinessId;
        await AsTenantAsync(a, null, run => run.ExecuteAsync(InsertPayment, a, a, a, a, a), commit: true);
        await AsTenantAsync(b, null, run => run.ExecuteAsync(InsertPayment, b, b, b, b, b), commit: true);

        await AsTenantAsync(a, null, async run =>
        {
            (await run.ScalarAsync("SELECT count(*) FROM payments WHERE business_id = @b", b)).ShouldBe(0);
            (await run.ScalarAsync("SELECT count(*) FROM payments")).ShouldBe(1);
        });

        // Append-only (ADR 0009): even on its own rows the application role can neither update nor delete.
        var update = await Should.ThrowAsync<PostgresException>(() =>
            AsTenantAsync(a, null, run => run.ExecuteAsync("UPDATE payments SET amount = 1 WHERE business_id = @b", a)));
        update.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        var delete = await Should.ThrowAsync<PostgresException>(() =>
            AsTenantAsync(a, null, run => run.ExecuteAsync("DELETE FROM payments WHERE business_id = @b", a)));
        delete.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);

        var crossInsert = await Should.ThrowAsync<PostgresException>(() =>
            AsTenantAsync(a, null, run => run.ExecuteAsync(
                "INSERT INTO payments (id, business_id, appointment_id, amount, currency, method, created_at) "
                + "VALUES (gen_random_uuid(), @b, gen_random_uuid(), 10, 'ARS', 'Cash', now())", b)));
        crossInsert.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);

        await AsTenantAsync(null, null, async run =>
            (await run.ScalarAsync("SELECT count(*) FROM payments")).ShouldBe(0));
    }

    [Fact]
    public async Task Cash_register_tables_are_isolated_and_append_only_with_plain_sql()
    {
        var a = _t.A.BusinessId;
        var b = _t.B.BusinessId;
        foreach (var tenant in new[] { a, b })
        {
            await AsTenantAsync(tenant, null, run => run.ExecuteAsync(InsertCashMovement, tenant), commit: true);
            await AsTenantAsync(tenant, null, run => run.ExecuteAsync(InsertCashClosing, tenant), commit: true);
        }

        await AsTenantAsync(a, null, async run =>
        {
            (await run.ScalarAsync("SELECT count(*) FROM cash_movements WHERE business_id = @b", b)).ShouldBe(0);
            (await run.ScalarAsync("SELECT count(*) FROM cash_movements")).ShouldBe(1);
            (await run.ScalarAsync("SELECT count(*) FROM cash_closings WHERE business_id = @b", b)).ShouldBe(0);
            (await run.ScalarAsync("SELECT count(*) FROM cash_closings")).ShouldBe(1);
        });

        // Append-only (ADR 0009): not even on its own rows.
        foreach (var statement in new[]
        {
            "UPDATE cash_movements SET concept = 'x' WHERE business_id = @b",
            "DELETE FROM cash_movements WHERE business_id = @b",
            "UPDATE cash_closings SET counted = 1 WHERE business_id = @b",
            "DELETE FROM cash_closings WHERE business_id = @b",
        })
        {
            var ex = await Should.ThrowAsync<PostgresException>(() =>
                AsTenantAsync(a, null, run => run.ExecuteAsync(statement, a)));
            ex.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        }

        var cross = await Should.ThrowAsync<PostgresException>(() =>
            AsTenantAsync(a, null, run => run.ExecuteAsync(InsertCashMovement, b)));
        cross.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);

        await AsTenantAsync(null, null, async run =>
        {
            (await run.ScalarAsync("SELECT count(*) FROM cash_movements")).ShouldBe(0);
            (await run.ScalarAsync("SELECT count(*) FROM cash_closings")).ShouldBe(0);
        });
    }

    [Fact]
    public async Task Schedule_tables_are_isolated_with_plain_sql()
    {
        var a = _t.A.BusinessId;
        var b = _t.B.BusinessId;
        foreach (var tenant in new[] { a, b })
        {
            await AsTenantAsync(tenant, null, run => run.ExecuteAsync(InsertWorkingHours, tenant, tenant), commit: true);
            await AsTenantAsync(tenant, null, run => run.ExecuteAsync(InsertScheduleException, tenant), commit: true);
        }

        await AsTenantAsync(a, null, async run =>
        {
            foreach (var table in ScheduleTables)
            {
                (await run.ScalarAsync($"SELECT count(*) FROM {table} WHERE business_id = @b", b)).ShouldBe(0);
                (await run.ScalarAsync($"SELECT count(*) FROM {table}")).ShouldBe(1);
            }

            (await run.ExecuteAsync("UPDATE working_hours SET end_minute = 1000 WHERE business_id = @b", b)).ShouldBe(0);
            (await run.ExecuteAsync("DELETE FROM schedule_exceptions WHERE business_id = @b", b)).ShouldBe(0);
        });

        var cross = await Should.ThrowAsync<PostgresException>(() =>
            AsTenantAsync(a, null, run => run.ExecuteAsync(InsertScheduleException, b)));
        cross.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);

        await AsTenantAsync(null, null, async run =>
        {
            (await run.ScalarAsync("SELECT count(*) FROM working_hours")).ShouldBe(0);
            (await run.ScalarAsync("SELECT count(*) FROM schedule_exceptions")).ShouldBe(0);
        });
    }

    private static readonly string[] ScheduleTables = ["working_hours", "schedule_exceptions"];

    private const string InsertWorkingHours = """
        WITH p AS (
            INSERT INTO professionals (id, business_id, name, is_active, created_at, updated_at)
            VALUES (gen_random_uuid(), @b, 'Profesional', true, now(), now()) RETURNING id)
        INSERT INTO working_hours (id, business_id, professional_id, day_of_week, start_minute, end_minute)
        SELECT gen_random_uuid(), @b, p.id, 1, 540, 1080 FROM p
        """;

    private const string InsertScheduleException = """
        INSERT INTO schedule_exceptions (id, business_id, professional_id, date, reason)
        VALUES (gen_random_uuid(), @b, NULL, current_date, 'Feriado')
        """;

    private const string InsertCashMovement = """
        INSERT INTO cash_movements (id, business_id, kind, amount, currency, concept, created_at)
        VALUES (gen_random_uuid(), @b, 'Income', 100, 'ARS', 'Cambio inicial', now())
        """;

    private const string InsertCashClosing = """
        INSERT INTO cash_closings (id, business_id, date, expected, counted, difference, currency, created_at)
        VALUES (gen_random_uuid(), @b, current_date, 100, 100, 0, 'ARS', now())
        """;

    private const string InsertPayment = """
        WITH c AS (
            INSERT INTO customers (id, business_id, name, is_archived, created_at, updated_at)
            VALUES (gen_random_uuid(), @b, 'Cliente', false, now(), now()) RETURNING id),
        p AS (
            INSERT INTO professionals (id, business_id, name, is_active, created_at, updated_at)
            VALUES (gen_random_uuid(), @b, 'Profesional', true, now(), now()) RETURNING id),
        s AS (
            INSERT INTO services (id, business_id, name, duration_minutes, price, currency, is_active, created_at, updated_at)
            VALUES (gen_random_uuid(), @b, 'Corte', 30, 100, 'ARS', true, now(), now()) RETURNING id),
        a AS (
            INSERT INTO appointments (id, business_id, customer_id, professional_id, service_id, starts_at, ends_at,
                status, price, currency, created_at, updated_at)
            SELECT gen_random_uuid(), @b, c.id, p.id, s.id, now(), now() + interval '30 minutes',
                'Scheduled', 100, 'ARS', now(), now()
            FROM c, p, s RETURNING id)
        INSERT INTO payments (id, business_id, appointment_id, amount, currency, method, created_at)
        SELECT gen_random_uuid(), @b, a.id, 100, 'ARS', 'Cash', now() FROM a
        """;

    private const string InsertAppointment = """
        WITH c AS (
            INSERT INTO customers (id, business_id, name, is_archived, created_at, updated_at)
            VALUES (gen_random_uuid(), @b, 'Cliente', false, now(), now()) RETURNING id),
        p AS (
            INSERT INTO professionals (id, business_id, name, is_active, created_at, updated_at)
            VALUES (gen_random_uuid(), @b, 'Profesional', true, now(), now()) RETURNING id),
        s AS (
            INSERT INTO services (id, business_id, name, duration_minutes, price, currency, is_active, created_at, updated_at)
            VALUES (gen_random_uuid(), @b, 'Corte', 30, 100, 'ARS', true, now(), now()) RETURNING id)
        INSERT INTO appointments (id, business_id, customer_id, professional_id, service_id, starts_at, ends_at,
            status, price, currency, created_at, updated_at)
        SELECT gen_random_uuid(), @b, c.id, p.id, s.id, now(), now() + interval '30 minutes',
            'Scheduled', 100, 'ARS', now(), now()
        FROM c, p, s
        """;

    private const string InsertProfessionalWithService = """
        WITH p AS (
            INSERT INTO professionals (id, business_id, name, is_active, created_at, updated_at)
            VALUES (gen_random_uuid(), @b, 'Profesional', true, now(), now()) RETURNING id),
        s AS (
            INSERT INTO services (id, business_id, name, duration_minutes, price, currency, is_active, created_at, updated_at)
            VALUES (gen_random_uuid(), @b, 'Corte', 30, 100, 'ARS', true, now(), now()) RETURNING id)
        INSERT INTO professional_services (business_id, professional_id, service_id)
        SELECT @b, p.id, s.id FROM p, s
        """;

    private const string InsertCustomer = """
        INSERT INTO customers (id, business_id, name, is_archived, created_at, updated_at)
        VALUES (gen_random_uuid(), @b, 'Cliente', false, now(), now())
        """;

    private const string InsertService = """
        INSERT INTO services (id, business_id, name, duration_minutes, price, currency, is_active, created_at, updated_at)
        VALUES (gen_random_uuid(), @b, 'Corte', 30, 100, 'ARS', true, now(), now())
        """;

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
