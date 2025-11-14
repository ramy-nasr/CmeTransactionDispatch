using System;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TransactionDispatch.Domain;
using TransactionDispatch.Infrastructure.Options;

namespace TransactionDispatch.Infrastructure.Messaging;

public sealed class KafkaMessageProducer : IKafkaProducer, IDisposable
{
    private readonly IProducer<string, byte[]> _producer;
    private readonly KafkaProducerOptions _options;
    private readonly ILogger<KafkaMessageProducer> _logger;
    private readonly string _topic;

    public KafkaMessageProducer(IOptions<KafkaProducerOptions> options, ILogger<KafkaMessageProducer> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_options.BootstrapServers))
        {
            throw new InvalidOperationException("Kafka producer bootstrap servers must be configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.Topic))
        {
            throw new InvalidOperationException("Kafka producer topic must be configured.");
        }

        _topic = _options.Topic!;

        var config = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            ClientId = string.IsNullOrWhiteSpace(_options.ClientId)
                ? $"transaction-dispatch-api-{Environment.MachineName}"
                : _options.ClientId,
            BatchSize = _options.BatchSizeBytes > 0 ? _options.BatchSizeBytes : null,
            LingerMs = _options.LingerMilliseconds > 0 ? _options.LingerMilliseconds : null,
            MessageTimeoutMs = _options.MessageTimeoutMilliseconds > 0 ? _options.MessageTimeoutMilliseconds : null
        };

        if (!string.IsNullOrWhiteSpace(_options.Acks)
            && Enum.TryParse<Acks>(_options.Acks, true, out var ackMode))
        {
            config.Acks = ackMode;
        }

        if (!string.IsNullOrWhiteSpace(_options.CompressionType)
            && Enum.TryParse<CompressionType>(_options.CompressionType, true, out var compression))
        {
            config.CompressionType = compression;
        }

        _producer = new ProducerBuilder<string, byte[]>(config).Build();
    }

    public async Task ProduceAsync(TransactionMessage message, CancellationToken cancellationToken)
    {
        var kafkaMessage = new Message<string, byte[]>
        {
            Key = message.FileName,
            Value = message.Payload,
            Headers = BuildHeaders(message.Headers),
            Timestamp = new Timestamp(DateTime.UtcNow)
        };

        try
        {
            var result = await _producer.ProduceAsync(_topic, kafkaMessage, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Produced message for {FileName} to partition {Partition} with offset {Offset}", message.FileName, result.Partition, result.Offset);
        }
        catch (ProduceException<string, byte[]> ex)
        {
            _logger.LogError(ex, "Failed to produce message for {FileName}", message.FileName);
            throw;
        }
    }

    private static Headers? BuildHeaders(IDictionary<string, string>? headers)
    {
        if (headers is null || headers.Count == 0)
        {
            return null;
        }

        var kafkaHeaders = new Headers();
        foreach (var header in headers)
        {
            kafkaHeaders.Add(header.Key, System.Text.Encoding.UTF8.GetBytes(header.Value));
        }

        return kafkaHeaders;
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(10));
        _producer.Dispose();
    }
}
