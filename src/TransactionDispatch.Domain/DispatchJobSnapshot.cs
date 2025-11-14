namespace TransactionDispatch.Domain;

public sealed record DispatchJobSnapshot(
    DispatchJobId Id,
    DispatchJobStatus Status,
    DispatchJobProgress Progress,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    string? FailureReason = null
);
