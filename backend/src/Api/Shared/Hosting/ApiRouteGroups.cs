namespace Api.Shared.Hosting;

/// <summary>
/// Route groups modules map their endpoints into.
/// <list type="bullet">
/// <item><see cref="Anonymous"/>: <c>/api</c> without auth (login, refresh, future public booking).
/// Endpoints here manage their own tenant scope.</item>
/// <item><see cref="Tenant"/>: <c>/api</c> requiring an access token bound to a business; each
/// request runs inside one transaction with the tenant applied (EF filters + RLS).</item>
/// </list>
/// Both groups validate request bodies automatically.
/// </summary>
public sealed record ApiRouteGroups(RouteGroupBuilder Anonymous, RouteGroupBuilder Tenant);
