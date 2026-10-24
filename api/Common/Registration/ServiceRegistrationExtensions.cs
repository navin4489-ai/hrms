using System.Reflection;

namespace Wintech.Hrms.Api.Common.Registration;

/// <summary>
/// Marker for application services (auth, approvals, leave, ...). Implementations are
/// auto-registered as scoped services by <see cref="ServiceRegistrationExtensions.AddAppServices"/>,
/// which lets feature modules be added without editing Program.cs.
/// Convention: implement exactly ONE interface besides this marker and register nothing yourself.
/// </summary>
public interface IAppService
{
}

/// <summary>Idempotent database seeder. Implementations are discovered automatically and run in <see cref="Order"/> sequence.</summary>
public interface IDataSeeder
{
    /// <summary>Lower runs first. Core reference/demo data uses 0-99; module data should use 100+.</summary>
    int Order { get; }

    Task SeedAsync(CancellationToken cancellationToken = default);
}

public static class ServiceRegistrationExtensions
{
    public static IServiceCollection AddAppServices(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }))
        {
            // NoopNotificationPublisher is explicitly registered as a fallback in Program.cs.
            // Do not auto-register it a second time: when assembly type order puts it after the
            // real notification implementation, it would win single-service resolution and
            // silently disable persisted in-app notifications.
            var implementsService = typeof(IAppService).IsAssignableFrom(type) &&
                                    type != typeof(Wintech.Hrms.Api.Common.Notifications.NoopNotificationPublisher);
            var implementsSeeder = typeof(IDataSeeder).IsAssignableFrom(type);

            if (implementsService)
            {
                var interfaces = type.GetInterfaces()
                    .Where(i => i != typeof(IAppService) && i != typeof(IDataSeeder))
                    .ToArray();

                if (interfaces.Length > 0)
                {
                    foreach (var contract in interfaces) services.AddScoped(contract, type);
                }
                services.AddScoped(type);
            }

            if (implementsSeeder) services.AddScoped(typeof(IDataSeeder), type);
        }

        return services;
    }
}
