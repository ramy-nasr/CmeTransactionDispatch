using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TransactionDispatch.Domain.Abstractions;
using TransactionDispatch.Infrastructure.FileSystem;
using TransactionDispatch.Infrastructure.Messaging;
using TransactionDispatch.Infrastructure.Options;
using TransactionDispatch.Infrastructure.Persistence;

namespace TransactionDispatch.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddTransactionDispatchInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<KafkaProducerOptions>()
            .Bind(configuration.GetSection("Kafka:Producer"))
            .Validate(options => !string.IsNullOrWhiteSpace(options.BootstrapServers), "Kafka bootstrap servers must be configured.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Topic), "Kafka topic must be configured.")
            .ValidateOnStart();

        services.AddSingleton<IDispatchJobRepository, InMemoryDispatchJobRepository>();
        services.AddSingleton<IFileDiscoveryService, FileSystemDiscoveryService>();
        services.AddSingleton<IKafkaProducer, KafkaMessageProducer>();

        return services;
    }
}
