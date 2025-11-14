namespace TransactionDispatch.Worker.Configuration;

public sealed class KafkaConsumerOptions
{
    public string? BootstrapServers { get; set; }

    public string? GroupId { get; set; }

    public string? Topic { get; set; }

    public bool EnableAutoCommit { get; set; }

    public int FetchMaxBytes { get; set; }

    public int MaxDegreeOfParallelism { get; set; } = 1;
}
