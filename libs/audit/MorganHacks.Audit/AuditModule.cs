using Microsoft.Extensions.DependencyInjection;

namespace MorganHacks.Audit;

/// <summary>
/// Registers the read side of the audit trail.
/// </summary>
/// <remarks>
/// Only the read side, because only the read side exists. Nothing needs to be
/// registered for the trail to be written — that happens in the database
/// whether this method was called or not, which is what makes the recording
/// hard to switch off by accident.
/// <para>
/// Expects an <c>NpgsqlDataSource</c> already in the container. The caller
/// owns the connection pool; a library that opened its own would double the
/// connections a service holds for no benefit.
/// </para>
/// </remarks>
public static class AuditModule
{
    public static IServiceCollection AddAuditTrail(this IServiceCollection services)
    {
        services.AddScoped<IAuditTrail, PostgresAuditTrail>();
        return services;
    }
}
