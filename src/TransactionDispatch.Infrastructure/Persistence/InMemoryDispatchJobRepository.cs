using System.Collections.Concurrent;
using TransactionDispatch.Domain;
using TransactionDispatch.Domain.Abstractions;

namespace TransactionDispatch.Infrastructure.Persistence;

public sealed class InMemoryDispatchJobRepository : IDispatchJobRepository
{
    private readonly ConcurrentDictionary<DispatchJobId, DispatchJob> _jobs = new();
    private readonly ConcurrentDictionary<string, DispatchJobId> _jobIdsByKey = new(StringComparer.OrdinalIgnoreCase);

    public Task<bool> AddAsync(DispatchJob job, CancellationToken cancellationToken)
    {
        var key = job.IdempotencyKey;
        if (key is not null)
        {
            if (!_jobIdsByKey.TryAdd(key, job.Id))
            {
                return Task.FromResult(false);
            }
        }

        if (_jobs.TryAdd(job.Id, job))
        {
            return Task.FromResult(true);
        }

        if (key is not null)
        {
            _jobIdsByKey.TryRemove(key, out _);
        }

        return Task.FromResult(false);
    }

    public Task UpdateAsync(DispatchJob job, CancellationToken cancellationToken)
    {
        _jobs[job.Id] = job;
        return Task.CompletedTask;
    }

    public Task<DispatchJob?> GetAsync(DispatchJobId jobId, CancellationToken cancellationToken)
    {
        _jobs.TryGetValue(jobId, out var job);
        return Task.FromResult(job);
    }

    public Task<DispatchJob?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Task.FromResult<DispatchJob?>(null);
        }

        var key = idempotencyKey.Trim();
        if (_jobIdsByKey.TryGetValue(key, out var jobId) && _jobs.TryGetValue(jobId, out var job))
        {
            return Task.FromResult<DispatchJob?>(job);
        }

        return Task.FromResult<DispatchJob?>(null);
    }

    public Task<DispatchJobSnapshot?> GetSnapshotAsync(DispatchJobId jobId, CancellationToken cancellationToken)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            return Task.FromResult<DispatchJobSnapshot?>(job.ToSnapshot());
        }

        return Task.FromResult<DispatchJobSnapshot?>(null);
    }
}
