using System.Data.Common;
using Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Api.Shared.Tenancy;

/// <summary>
/// Second isolation layer (with RLS). Every time EF starts a transaction, copies the current
/// tenant into transaction-local Postgres settings:
/// <c>set_config('app.business_id', ..., true)</c> and <c>set_config('app.user_id', ..., true)</c>.
/// Because they are transaction-local (<c>is_local = true</c>) they can never leak to another
/// request through the connection pool. Outside a transaction no tenant is set, so RLS returns
/// nothing: reads of business data must happen inside a transaction (see TenantTransactionFilter).
/// </summary>
public sealed class TenantTransactionInterceptor(ITenantContext tenant) : DbTransactionInterceptor
{
    private const string Sql =
        $"SELECT set_config('{TenantSettings.BusinessId}', @business_id, true), " +
        $"set_config('{TenantSettings.UserId}', @user_id, true)";

    public override DbTransaction TransactionStarted(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        using var command = CreateCommand(connection, result);
        command.ExecuteNonQuery();
        return result;
    }

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        await using var command = CreateCommand(connection, result);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return result;
    }

    private NpgsqlCommand CreateCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = new NpgsqlCommand(Sql, (NpgsqlConnection)connection, (NpgsqlTransaction)transaction);
        command.Parameters.AddWithValue("business_id", tenant.BusinessId?.ToString() ?? string.Empty);
        command.Parameters.AddWithValue("user_id", tenant.UserId?.ToString() ?? string.Empty);
        return command;
    }
}
