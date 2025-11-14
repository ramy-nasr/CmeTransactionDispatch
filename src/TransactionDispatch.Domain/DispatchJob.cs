namespace TransactionDispatch.Domain;

public sealed class DispatchJob
{
    private DispatchJobProgress _progress;
    private DispatchJobStatus _status;
    private DateTimeOffset? _completedAt;
    private string? _failureReason;

    public DispatchJob(
        DispatchJobId id,
        string folderPath,
        bool deleteAfterSend,
        IReadOnlyCollection<string> allowedExtensions,
        string? idempotencyKey = null)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("Folder path must be provided", nameof(folderPath));
        }

        Id = id;
        FolderPath = folderPath;
        DeleteAfterSend = deleteAfterSend;
        AllowedExtensions = allowedExtensions ?? Array.Empty<string>();
        IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        CreatedAt = DateTimeOffset.UtcNow;
        _progress = new DispatchJobProgress(0, 0, 0, 0);
        _status = DispatchJobStatus.Pending;
    }

    public DispatchJobId Id { get; }

    public string FolderPath { get; }

    public bool DeleteAfterSend { get; }

    public IReadOnlyCollection<string> AllowedExtensions { get; }

    public string? IdempotencyKey { get; }

    public DispatchJobStatus Status => _status;

    public DispatchJobProgress Progress => _progress;

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset? CompletedAt => _completedAt;

    public string? FailureReason => _failureReason;

    public void Start()
    {
        if (_status is DispatchJobStatus.Completed or DispatchJobStatus.Failed)
        {
            throw new InvalidOperationException("Job has already finished.");
        }

        _status = DispatchJobStatus.Running;
    }

    public void AddExpectedFiles(int count)
    {
        if (count <= 0)
        {
            return;
        }

        _progress = _progress.IncrementTotal(count);
    }

    public void RecordResult(bool success)
    {
        _progress = _progress.IncrementProcessed(success);
    }

    public void Complete()
    {
        _status = DispatchJobStatus.Completed;
        _completedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string reason)
    {
        _status = DispatchJobStatus.Failed;
        _completedAt = DateTimeOffset.UtcNow;
        _failureReason = reason;
    }

    public DispatchJobSnapshot ToSnapshot() => new(
        Id,
        _status,
        _progress,
        CreatedAt,
        _completedAt,
        _failureReason);
}
