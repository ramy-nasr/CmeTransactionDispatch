namespace TransactionDispatch.Domain.Abstractions;

public interface IDispatchJobRepository
{
    Task<bool> AddAsync(DispatchJob job, CancellationToken cancellationToken);

    Task UpdateAsync(DispatchJob job, CancellationToken cancellationToken);

    Task<DispatchJob?> GetAsync(DispatchJobId jobId, CancellationToken cancellationToken);

    Task<DispatchJob?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    Task<DispatchJobSnapshot?> GetSnapshotAsync(DispatchJobId jobId, CancellationToken cancellationToken);
}
