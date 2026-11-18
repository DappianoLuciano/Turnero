using Api.Modules.Appointments.Domain;
using Api.Modules.Businesses.Domain;
using Api.Modules.CashRegister.Domain;
using Api.Modules.Customers.Domain;
using Api.Modules.Payments.Domain;
using Api.Modules.Schedules.Domain;
using Api.Modules.Services.Domain;
using Api.Modules.Staff.Domain;
using Api.Modules.Users.Domain;
using Api.Shared.Auth;
using Api.Shared.Monetary;
using Api.Shared.Tenancy;
using Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Api.Tests.Tenancy;

/// <summary>
/// With the context of business A, nothing of business B can be read or modified through EF —
/// not even when the EF query filter or the EF write guard are bypassed (RLS is the second layer).
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Integration")]
public sealed class EfTenantIsolationTests(PostgresFixture db) : IAsyncLifetime
{
    private TwoTenants _t = null!;

    public async ValueTask InitializeAsync() => _t = await TwoTenants.CreateAsync(db);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Reads_only_rows_of_the_current_tenant()
    {
        await using var ctx = TenantDb.Create(db, _t.A.BusinessId);
        await using var tx = await ctx.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        var memberships = await ctx.Set<BusinessMembership>().ToListAsync(TestContext.Current.CancellationToken);
        memberships.Count.ShouldBe(2);
        memberships.ShouldAllBe(m => m.BusinessId == _t.A.BusinessId);

        var businesses = await ctx.Set<Business>().ToListAsync(TestContext.Current.CancellationToken);
        businesses.Select(b => b.Id).ShouldBe([_t.A.BusinessId]);
    }

    [Fact]
    public async Task Rls_hides_other_tenant_even_when_ef_filter_is_ignored()
    {
        await using var ctx = TenantDb.Create(db, _t.A.BusinessId);
        await using var tx = await ctx.Database.BeginTransactionAsync(TestContext.Current.CancellationToken);

        var memberships = await ctx.Set<BusinessMembership>().IgnoreQueryFilters()
            .Where(m => m.BusinessId == _t.B.BusinessId)
            .ToListAsync(TestContext.Current.CancellationToken);
        memberships.ShouldBeEmpty();

        var allVisibleBusinesses = await ctx.Set<Business>().IgnoreQueryFilters()
            .Select(b => b.Id).ToListAsync(TestContext.Current.CancellationToken);
        allVisibleBusinesses.ShouldBe([_t.A.BusinessId]);
    }

    [Fact]
    public async Task Bulk_update_and_delete_of_other_tenant_affect_zero_rows()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);

            var updated = await ctx.Set<BusinessMembership>().IgnoreQueryFilters()
                .Where(m => m.BusinessId == _t.B.BusinessId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.Role, BusinessRole.Reception), ct);
            var deleted = await ctx.Set<BusinessMembership>().IgnoreQueryFilters()
                .Where(m => m.BusinessId == _t.B.BusinessId)
                .ExecuteDeleteAsync(ct);
            var businessRenamed = await ctx.Set<Business>().IgnoreQueryFilters()
                .Where(b => b.Id == _t.B.BusinessId)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Name, "hacked"), ct);

            updated.ShouldBe(0);
            deleted.ShouldBe(0);
            businessRenamed.ShouldBe(0);
            await tx.CommitAsync(ct);
        }

        await AssertTenantBUntouchedAsync();
    }

    [Fact]
    public async Task Ef_guard_rejects_modifying_an_entity_of_other_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid victimId;
        await using (var ctxB = TenantDb.Create(db, _t.B.BusinessId))
        {
            await using var txB = await ctxB.Database.BeginTransactionAsync(ct);
            victimId = (await ctxB.Set<BusinessMembership>().FirstAsync(ct)).Id;
        }

        await using var ctx = TenantDb.Create(db, _t.A.BusinessId);
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        var forged = BusinessMembership.Create(_t.B.BusinessId, _t.B.StaffId, BusinessRole.Owner, DateTimeOffset.UtcNow);
        ctx.Entry(forged).Property(m => m.Id).CurrentValue = victimId;
        ctx.Attach(forged).State = EntityState.Modified;

        await Should.ThrowAsync<CrossTenantAccessException>(() => ctx.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Ef_guard_rejects_inserting_rows_for_other_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = TenantDb.Create(db, _t.A.BusinessId);
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        ctx.Add(BusinessMembership.Create(_t.B.BusinessId, _t.A.OwnerId, BusinessRole.Owner, DateTimeOffset.UtcNow));

        await Should.ThrowAsync<CrossTenantAccessException>(() => ctx.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Rls_rejects_insert_for_other_tenant_when_ef_guard_is_absent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = TenantDb.Create(db, _t.A.BusinessId, withGuard: false);
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        ctx.Add(BusinessMembership.Create(_t.B.BusinessId, _t.A.OwnerId, BusinessRole.Owner, DateTimeOffset.UtcNow));

        var ex = await Should.ThrowAsync<DbUpdateException>(() => ctx.SaveChangesAsync(ct));
        ex.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task Rls_rejects_update_of_other_tenant_when_ef_guard_is_absent()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid victimId;
        await using (var ctxB = TenantDb.Create(db, _t.B.BusinessId))
        {
            await using var txB = await ctxB.Database.BeginTransactionAsync(ct);
            victimId = (await ctxB.Set<BusinessMembership>().FirstAsync(ct)).Id;
        }

        await using var ctx = TenantDb.Create(db, _t.A.BusinessId, withGuard: false);
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        var forged = BusinessMembership.Create(_t.B.BusinessId, _t.B.StaffId, BusinessRole.Owner, DateTimeOffset.UtcNow);
        ctx.Entry(forged).Property(m => m.Id).CurrentValue = victimId;
        ctx.Attach(forged).State = EntityState.Modified;

        // RLS hides the row: the UPDATE matches 0 rows and EF reports a concurrency failure.
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => ctx.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Without_tenant_nothing_is_visible()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = TenantDb.Create(db, businessId: null);
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);

        (await ctx.Set<BusinessMembership>().IgnoreQueryFilters().CountAsync(ct)).ShouldBe(0);
        (await ctx.Set<Business>().IgnoreQueryFilters().CountAsync(ct)).ShouldBe(0);
    }

    [Fact]
    public async Task Outside_a_transaction_rls_returns_nothing()
    {
        // Settings are transaction-local: a query without a transaction has no tenant (fail closed).
        await using var ctx = TenantDb.Create(db, _t.A.BusinessId);
        (await ctx.Set<BusinessMembership>().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task Services_are_isolated_by_ef_filter_rls_and_guard()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedServiceAsync(_t.A.BusinessId, "Servicio A");
        await SeedServiceAsync(_t.B.BusinessId, "Servicio B");

        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            (await ctx.Set<Service>().Select(s => s.Name).ToListAsync(ct)).ShouldBe(["Servicio A"]);
            // RLS alone, with the EF filter bypassed.
            (await ctx.Set<Service>().IgnoreQueryFilters().Where(s => s.BusinessId == _t.B.BusinessId)
                .CountAsync(ct)).ShouldBe(0);
            (await ctx.Set<Service>().IgnoreQueryFilters().Where(s => s.BusinessId == _t.B.BusinessId)
                .ExecuteDeleteAsync(ct)).ShouldBe(0);
        }

        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            ctx.Add(Service.Create(_t.B.BusinessId, "Colado", null, 30, new Money(1m), DateTimeOffset.UtcNow));
            await Should.ThrowAsync<CrossTenantAccessException>(() => ctx.SaveChangesAsync(ct));
        }

        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId, withGuard: false))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            ctx.Add(Service.Create(_t.B.BusinessId, "Colado", null, 30, new Money(1m), DateTimeOffset.UtcNow));
            var ex = await Should.ThrowAsync<DbUpdateException>(() => ctx.SaveChangesAsync(ct));
            ex.InnerException.ShouldBeOfType<PostgresException>().SqlState
                .ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        }

        await using (var ctxB = TenantDb.Create(db, _t.B.BusinessId))
        {
            await using var tx = await ctxB.Database.BeginTransactionAsync(ct);
            (await ctxB.Set<Service>().Select(s => s.Name).ToListAsync(ct)).ShouldBe(["Servicio B"]);
        }
    }

    private async Task SeedServiceAsync(Guid businessId, string name)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = TenantDb.Create(db, businessId);
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        ctx.Add(Service.Create(businessId, name, null, 30, new Money(1000m), DateTimeOffset.UtcNow));
        await ctx.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    [Fact]
    public async Task Customers_are_isolated_by_ef_filter_rls_and_guard()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedCustomerAsync(_t.A.BusinessId, "Cliente A");
        await SeedCustomerAsync(_t.B.BusinessId, "Cliente B");

        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            (await ctx.Set<Customer>().Select(c => c.Name).ToListAsync(ct)).ShouldBe(["Cliente A"]);
            // RLS alone, with the EF filter bypassed.
            (await ctx.Set<Customer>().IgnoreQueryFilters().Where(c => c.BusinessId == _t.B.BusinessId)
                .CountAsync(ct)).ShouldBe(0);
            (await ctx.Set<Customer>().IgnoreQueryFilters().Where(c => c.BusinessId == _t.B.BusinessId)
                .ExecuteDeleteAsync(ct)).ShouldBe(0);
        }

        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            ctx.Add(Customer.Create(_t.B.BusinessId, "Colado", null, null, null, DateTimeOffset.UtcNow));
            await Should.ThrowAsync<CrossTenantAccessException>(() => ctx.SaveChangesAsync(ct));
        }

        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId, withGuard: false))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            ctx.Add(Customer.Create(_t.B.BusinessId, "Colado", null, null, null, DateTimeOffset.UtcNow));
            var ex = await Should.ThrowAsync<DbUpdateException>(() => ctx.SaveChangesAsync(ct));
            ex.InnerException.ShouldBeOfType<PostgresException>().SqlState
                .ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        }

        await using (var ctxB = TenantDb.Create(db, _t.B.BusinessId))
        {
            await using var tx = await ctxB.Database.BeginTransactionAsync(ct);
            (await ctxB.Set<Customer>().Select(c => c.Name).ToListAsync(ct)).ShouldBe(["Cliente B"]);
        }
    }

    [Fact]
    public async Task Professionals_and_their_services_are_isolated_by_ef_filter_rls_and_guard()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedProfessionalAsync(_t.A.BusinessId, "Profesional A");
        await SeedProfessionalAsync(_t.B.BusinessId, "Profesional B");

        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            (await ctx.Set<Professional>().Select(p => p.Name).ToListAsync(ct)).ShouldBe(["Profesional A"]);
            (await ctx.Set<ProfessionalService>().CountAsync(ct)).ShouldBe(1);
            // RLS alone, with the EF filter bypassed.
            (await ctx.Set<Professional>().IgnoreQueryFilters().Where(p => p.BusinessId == _t.B.BusinessId)
                .CountAsync(ct)).ShouldBe(0);
            (await ctx.Set<ProfessionalService>().IgnoreQueryFilters().Where(l => l.BusinessId == _t.B.BusinessId)
                .CountAsync(ct)).ShouldBe(0);
            (await ctx.Set<Professional>().IgnoreQueryFilters().Where(p => p.BusinessId == _t.B.BusinessId)
                .ExecuteDeleteAsync(ct)).ShouldBe(0);
        }

        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            ctx.Add(Professional.Create(_t.B.BusinessId, "Colado", DateTimeOffset.UtcNow));
            await Should.ThrowAsync<CrossTenantAccessException>(() => ctx.SaveChangesAsync(ct));
        }

        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId, withGuard: false))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            ctx.Add(Professional.Create(_t.B.BusinessId, "Colado", DateTimeOffset.UtcNow));
            var ex = await Should.ThrowAsync<DbUpdateException>(() => ctx.SaveChangesAsync(ct));
            ex.InnerException.ShouldBeOfType<PostgresException>().SqlState
                .ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        }

        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            ctx.Add(ProfessionalService.Create(_t.B.BusinessId, Guid.CreateVersion7(), Guid.CreateVersion7()));
            await Should.ThrowAsync<CrossTenantAccessException>(() => ctx.SaveChangesAsync(ct));
        }

        await using (var ctxB = TenantDb.Create(db, _t.B.BusinessId))
        {
            await using var tx = await ctxB.Database.BeginTransactionAsync(ct);
            (await ctxB.Set<Professional>().Select(p => p.Name).ToListAsync(ct)).ShouldBe(["Profesional B"]);
        }
    }

    [Fact]
    public async Task Appointments_are_isolated_by_ef_filter_rls_and_guard()
    {
        var ct = TestContext.Current.CancellationToken;
        var idsB = await SeedAppointmentAsync(_t.B.BusinessId);
        var idsA = await SeedAppointmentAsync(_t.A.BusinessId);

        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            (await ctx.Set<Appointment>().CountAsync(ct)).ShouldBe(1);
            // RLS alone, with the EF filter bypassed.
            (await ctx.Set<Appointment>().IgnoreQueryFilters().Where(a => a.BusinessId == _t.B.BusinessId)
                .CountAsync(ct)).ShouldBe(0);
            (await ctx.Set<Appointment>().IgnoreQueryFilters().Where(a => a.BusinessId == _t.B.BusinessId)
                .ExecuteDeleteAsync(ct)).ShouldBe(0);
        }

        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            ctx.Add(Appointment.Create(_t.B.BusinessId, idsB.Customer, idsB.Professional, idsB.Service,
                DateTimeOffset.UtcNow, 30, new Money(1m), null, DateTimeOffset.UtcNow));
            await Should.ThrowAsync<CrossTenantAccessException>(() => ctx.SaveChangesAsync(ct));
        }

        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId, withGuard: false))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            ctx.Add(Appointment.Create(_t.B.BusinessId, idsB.Customer, idsB.Professional, idsB.Service,
                DateTimeOffset.UtcNow, 30, new Money(1m), null, DateTimeOffset.UtcNow));
            var ex = await Should.ThrowAsync<DbUpdateException>(() => ctx.SaveChangesAsync(ct));
            ex.InnerException.ShouldBeOfType<PostgresException>().SqlState
                .ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        }

        await using (var ctxB = TenantDb.Create(db, _t.B.BusinessId))
        {
            await using var tx = await ctxB.Database.BeginTransactionAsync(ct);
            (await ctxB.Set<Appointment>().CountAsync(ct)).ShouldBe(1);
        }

        idsA.Customer.ShouldNotBe(idsB.Customer);
    }

    [Fact]
    public async Task Payments_are_isolated_by_ef_filter_rls_and_guard()
    {
        var ct = TestContext.Current.CancellationToken;
        var appointmentB = await SeedPaymentAsync(_t.B.BusinessId);
        await SeedPaymentAsync(_t.A.BusinessId);

        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            (await ctx.Set<Payment>().CountAsync(ct)).ShouldBe(1);
            // RLS alone, with the EF filter bypassed.
            (await ctx.Set<Payment>().IgnoreQueryFilters().Where(p => p.BusinessId == _t.B.BusinessId)
                .CountAsync(ct)).ShouldBe(0);
        }

        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            ctx.Add(Payment.Collect(_t.B.BusinessId, appointmentB, new Money(1m), PaymentMethod.Cash, null,
                DateTimeOffset.UtcNow));
            await Should.ThrowAsync<CrossTenantAccessException>(() => ctx.SaveChangesAsync(ct));
        }

        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId, withGuard: false))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            ctx.Add(Payment.Collect(_t.B.BusinessId, appointmentB, new Money(1m), PaymentMethod.Cash, null,
                DateTimeOffset.UtcNow));
            var ex = await Should.ThrowAsync<DbUpdateException>(() => ctx.SaveChangesAsync(ct));
            ex.InnerException.ShouldBeOfType<PostgresException>().SqlState
                .ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        }

        await using (var ctxB = TenantDb.Create(db, _t.B.BusinessId))
        {
            await using var tx = await ctxB.Database.BeginTransactionAsync(ct);
            (await ctxB.Set<Payment>().CountAsync(ct)).ShouldBe(1);
        }
    }

    [Fact]
    public async Task Cash_register_rows_are_isolated_by_ef_filter_rls_and_guard()
    {
        var ct = TestContext.Current.CancellationToken;
        foreach (var businessId in new[] { _t.A.BusinessId, _t.B.BusinessId })
        {
            await using var seed = TenantDb.Create(db, businessId);
            await using var seedTx = await seed.Database.BeginTransactionAsync(ct);
            var now = DateTimeOffset.UtcNow;
            seed.Add(CashMovement.Register(businessId, CashKind.Income, new Money(10m), "Cambio", null, now));
            seed.Add(CashClosing.Close(businessId, DateOnly.FromDateTime(DateTime.UtcNow), new Money(10m), new Money(10m),
                null, null, now));
            await seed.SaveChangesAsync(ct);
            await seedTx.CommitAsync(ct);
        }

        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            (await ctx.Set<CashMovement>().CountAsync(ct)).ShouldBe(1);
            (await ctx.Set<CashClosing>().CountAsync(ct)).ShouldBe(1);
            // RLS alone, with the EF filter bypassed.
            (await ctx.Set<CashMovement>().IgnoreQueryFilters().Where(m => m.BusinessId == _t.B.BusinessId)
                .CountAsync(ct)).ShouldBe(0);
            (await ctx.Set<CashClosing>().IgnoreQueryFilters().Where(c => c.BusinessId == _t.B.BusinessId)
                .CountAsync(ct)).ShouldBe(0);
        }

        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            ctx.Add(CashMovement.Register(_t.B.BusinessId, CashKind.Income, new Money(1m), "Intruso", null,
                DateTimeOffset.UtcNow));
            await Should.ThrowAsync<CrossTenantAccessException>(() => ctx.SaveChangesAsync(ct));
        }

        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId, withGuard: false))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            ctx.Add(CashMovement.Register(_t.B.BusinessId, CashKind.Income, new Money(1m), "Intruso", null,
                DateTimeOffset.UtcNow));
            var ex = await Should.ThrowAsync<DbUpdateException>(() => ctx.SaveChangesAsync(ct));
            ex.InnerException.ShouldBeOfType<PostgresException>().SqlState
                .ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    [Fact]
    public async Task Schedule_rows_are_isolated_by_ef_filter_rls_and_guard()
    {
        var ct = TestContext.Current.CancellationToken;
        var professionals = new Dictionary<Guid, Guid>();
        foreach (var businessId in new[] { _t.A.BusinessId, _t.B.BusinessId })
        {
            await using var seed = TenantDb.Create(db, businessId);
            await using var seedTx = await seed.Database.BeginTransactionAsync(ct);
            var professional = Professional.Create(businessId, "Profesional", DateTimeOffset.UtcNow);
            seed.Add(professional);
            await seed.SaveChangesAsync(ct);
            seed.Add(WorkingBlock.Create(businessId, professional.Id, 1, 540, 1080));
            seed.Add(ScheduleException.Create(businessId, null, new DateOnly(2026, 12, 25), "Navidad"));
            await seed.SaveChangesAsync(ct);
            await seedTx.CommitAsync(ct);
            professionals[businessId] = professional.Id;
        }

        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            (await ctx.Set<WorkingBlock>().CountAsync(ct)).ShouldBe(1);
            (await ctx.Set<ScheduleException>().CountAsync(ct)).ShouldBe(1);
            // RLS alone, with the EF filter bypassed.
            (await ctx.Set<WorkingBlock>().IgnoreQueryFilters().Where(b => b.BusinessId == _t.B.BusinessId)
                .CountAsync(ct)).ShouldBe(0);
            (await ctx.Set<ScheduleException>().IgnoreQueryFilters().Where(e => e.BusinessId == _t.B.BusinessId)
                .ExecuteDeleteAsync(ct)).ShouldBe(0);
        }

        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            ctx.Add(WorkingBlock.Create(_t.B.BusinessId, professionals[_t.B.BusinessId], 2, 540, 600));
            await Should.ThrowAsync<CrossTenantAccessException>(() => ctx.SaveChangesAsync(ct));
        }

        await using (var ctx = TenantDb.Create(db, _t.A.BusinessId, withGuard: false))
        {
            await using var tx = await ctx.Database.BeginTransactionAsync(ct);
            ctx.Add(WorkingBlock.Create(_t.B.BusinessId, professionals[_t.B.BusinessId], 2, 540, 600));
            var ex = await Should.ThrowAsync<DbUpdateException>(() => ctx.SaveChangesAsync(ct));
            ex.InnerException.ShouldBeOfType<PostgresException>().SqlState
                .ShouldBe(PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    /// <summary>Seeds customer, professional, service, appointment and one payment; returns the appointment id.</summary>
    private async Task<Guid> SeedPaymentAsync(Guid businessId)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = TenantDb.Create(db, businessId);
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var customer = Customer.Create(businessId, "Cliente", null, null, null, now);
        var professional = Professional.Create(businessId, "Profesional", now);
        var service = Service.Create(businessId, "Corte", null, 30, new Money(10m), now);
        ctx.AddRange(customer, professional, service);
        await ctx.SaveChangesAsync(ct);
        var appointment = Appointment.Create(
            businessId, customer.Id, professional.Id, service.Id, now, 30, new Money(10m), null, now);
        ctx.Add(appointment);
        await ctx.SaveChangesAsync(ct);
        ctx.Add(Payment.Collect(businessId, appointment.Id, new Money(10m), PaymentMethod.Cash, null, now));
        await ctx.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return appointment.Id;
    }

    private async Task<(Guid Customer, Guid Professional, Guid Service)> SeedAppointmentAsync(Guid businessId)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = TenantDb.Create(db, businessId);
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var customer = Customer.Create(businessId, "Cliente", null, null, null, now);
        var professional = Professional.Create(businessId, "Profesional", now);
        var service = Service.Create(businessId, "Corte", null, 30, new Money(1m), now);
        ctx.AddRange(customer, professional, service);
        await ctx.SaveChangesAsync(ct);
        ctx.Add(Appointment.Create(businessId, customer.Id, professional.Id, service.Id, now, 30, new Money(1m), null, now));
        await ctx.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (customer.Id, professional.Id, service.Id);
    }

    private async Task SeedProfessionalAsync(Guid businessId, string name)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = TenantDb.Create(db, businessId);
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        var professional = Professional.Create(businessId, name, DateTimeOffset.UtcNow);
        var service = Service.Create(businessId, "Corte", null, 30, new Money(1m), DateTimeOffset.UtcNow);
        ctx.Add(professional);
        ctx.Add(service);
        await ctx.SaveChangesAsync(ct);
        ctx.Add(ProfessionalService.Create(businessId, professional.Id, service.Id));
        await ctx.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task SeedCustomerAsync(Guid businessId, string name)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctx = TenantDb.Create(db, businessId);
        await using var tx = await ctx.Database.BeginTransactionAsync(ct);
        ctx.Add(Customer.Create(businessId, name, null, null, null, DateTimeOffset.UtcNow));
        await ctx.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task AssertTenantBUntouchedAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var ctxB = TenantDb.Create(db, _t.B.BusinessId);
        await using var txB = await ctxB.Database.BeginTransactionAsync(ct);
        var memberships = await ctxB.Set<BusinessMembership>().ToListAsync(ct);
        memberships.Count.ShouldBe(2);
        memberships.ShouldNotContain(m => m.Role == BusinessRole.Reception);
        (await ctxB.Set<Business>().SingleAsync(ct)).Name.ShouldBe("Business B");
    }
}
