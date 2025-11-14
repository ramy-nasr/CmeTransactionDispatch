using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
            .ValidateOnStart();

        services.AddHostedService<KafkaConsumerWorker>();
    })
    .Build();

await host.RunAsync();
