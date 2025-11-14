namespace TransactionDispatch.Infrastructure.Options;

public sealed class KafkaProducerOptions
{
    public string? BootstrapServers { get; set; }

    public string? Topic { get; set; }

    public string? ClientId { get; set; }

    public string? Acks { get; set; }

    public int BatchSizeBytes { get; set; }

    public int LingerMilliseconds { get; set; }

    public int MessageTimeoutMilliseconds { get; set; }

    public string? CompressionType { get; set; }
}
