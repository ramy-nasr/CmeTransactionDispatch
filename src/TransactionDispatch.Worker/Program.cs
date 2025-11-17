using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TransactionDispatch.Worker.Configuration;
using TransactionDispatch.Worker.Services;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services
            .AddOptions<KafkaConsumerOptions>()
            .Bind(context.Configuration.GetSection("Kafka:Consumer"))
            .Validate(options => !string.IsNullOrWhiteSpace(options.BootstrapServers), "Kafka consumer bootstrap servers must be configured.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Topic), "Kafka consumer topic must be configured.")
            .Validate(options => options.MaxDegreeOfParallelism > 0, "MaxDegreeOfParallelism must be greater than zero.")
            .Validate(options => options.MaxProcessingRetries > 0, "MaxProcessingRetries must be at least 1.")
            .Validate(options => options.RetryBackoffMilliseconds >= 0, "RetryBackoffMilliseconds cannot be negative.")
            .ValidateOnStart();

        services.AddHostedService<KafkaConsumerWorker>();
    })
    .Build();

await host.RunAsync();
