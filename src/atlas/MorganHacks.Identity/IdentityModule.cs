using Microsoft.Extensions.DependencyInjection;
using MorganHacks.Identity.Data;
using MorganHacks.Identity.Services;
using Npgsql;

namespace MorganHacks.Identity;

/// <summary>
/// Everything the Identity module needs, registered in one place.
/// </summary>
/// <remarks>
/// The API calls this rather than reaching inside the module, which is what
/// keeps the module extractable into its own service later.
/// </remarks>
public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(_ => new NpgsqlDataSourceBuilder(connectionString).Build());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IIdentityStore, PostgresIdentityStore>();
        services.AddScoped<MagicLinkService>();
        services.AddScoped<SessionService>();
        return services;
    }
}
