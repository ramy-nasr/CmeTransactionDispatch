namespace TransactionDispatch.Worker.Configuration;

public sealed class KafkaConsumerOptions
{
    public string? BootstrapServers { get; set; }

    public string? GroupId { get; set; }

    public string? Topic { get; set; }

    public bool EnableAutoCommit { get; set; }

    public int FetchMaxBytes { get; set; }

    public int MaxDegreeOfParallelism { get; set; } = 1;
    public int MaxProcessingRetries { get; set; } = 3;

    public int RetryBackoffMilliseconds { get; set; } = 1000;
}
