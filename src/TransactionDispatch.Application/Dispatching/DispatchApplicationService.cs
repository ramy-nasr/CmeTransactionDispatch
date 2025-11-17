using Microsoft.Extensions.Logging;
using System.IO;
using TransactionDispatch.Domain;
using TransactionDispatch.Domain.Abstractions;
using TransactionDispatch.Domain.FileSystem;
using TransactionDispatch.Domain.Messaging;

namespace TransactionDispatch.Application.Dispatching;

public sealed class DispatchApplicationService : IDispatchApplicationService
{
    private readonly IDispatchJobRepository _repository;
    private readonly IFileDiscoveryService _fileDiscoveryService;
    private readonly IKafkaProducer _kafkaProducer;
    private readonly ILogger<DispatchApplicationService> _logger;

    public DispatchApplicationService(
        IDispatchJobRepository repository,
        IFileDiscoveryService fileDiscoveryService,
        IKafkaProducer kafkaProducer,
        ILogger<DispatchApplicationService> logger)
    {
        _repository = repository;
        _fileDiscoveryService = fileDiscoveryService;
        _kafkaProducer = kafkaProducer;
        _logger = logger;
    }

    public async Task<DispatchJobId> DispatchTransactionsAsync(DispatchTransactionsCommand command, List<string> allowedExtensions, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.FolderPath))
        {
            throw new ArgumentException("Folder path is required", nameof(command));
        }

        var idempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey)
            ? null
            : command.IdempotencyKey.Trim();

        if (idempotencyKey is not null)
        {
            var existing = await _repository.GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                return existing.Id;
            }
        }

        var jobId = DispatchJobId.NewId();

        var job = new DispatchJob(jobId, command.FolderPath, command.DeleteAfterSend, allowedExtensions, idempotencyKey);
        var added = await _repository.AddAsync(job, cancellationToken).ConfigureAwait(false);
        if (!added)
        {
            if (idempotencyKey is null)
            {
                throw new InvalidOperationException($"Job with id {jobId} already exists.");
            }

            var existing = await _repository.GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken)
                .ConfigureAwait(false);
            if (existing is null)
            {
                throw new InvalidOperationException($"Failed to add dispatch job for idempotency key '{idempotencyKey}'.");
            }

            return existing.Id;
        }

        _ = Task.Run(() => ProcessJobAsync(job.Id, CancellationToken.None));

        return jobId;
    }

    public Task<DispatchJobSnapshot?> GetJobStatusAsync(DispatchJobId jobId, CancellationToken cancellationToken)
    {
        return _repository.GetSnapshotAsync(jobId, cancellationToken);
    }

    private async Task ProcessJobAsync(DispatchJobId jobId, CancellationToken cancellationToken)
    {
        DispatchJob? job = await _repository.GetAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return;
        }

        try
        {
            job.Start();
            await _repository.UpdateAsync(job, cancellationToken).ConfigureAwait(false);

            var files = await _fileDiscoveryService.FindFilesAsync(job, cancellationToken).ConfigureAwait(false);
            if (files.Count == 0)
            {
                job.Complete();
                await _repository.UpdateAsync(job, cancellationToken).ConfigureAwait(false);
                return;
            }

            foreach (var file in files)
            {
                var success = await ProcessFileAsync(job, file, cancellationToken).ConfigureAwait(false);
                job.RecordResult(success);
                await _repository.UpdateAsync(job, cancellationToken).ConfigureAwait(false);
            }

            if (job.Progress.Failed > 0)
            {
                job.Fail($"{job.Progress.Failed} files failed to publish.");
            }
            else
            {
                job.Complete();
            }

            await _repository.UpdateAsync(job, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed", jobId);
            job.Fail(ex.Message);
            await _repository.UpdateAsync(job, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> ProcessFileAsync(DispatchJob job, FileEntry file, CancellationToken cancellationToken)
    {
        try
        {
            var payload = await File.ReadAllBytesAsync(file.FullPath, cancellationToken).ConfigureAwait(false);
            var message = new TransactionMessage(
                file.Name,
                payload,
                file.ContentType,
                new Dictionary<string, string>
                {
                    ["source-file"] = file.FullPath,
                    ["job-id"] = job.Id.ToString()
                });

            await _kafkaProducer.ProduceAsync(message, cancellationToken).ConfigureAwait(false);

            if (job.DeleteAfterSend)
            {
                TryDeleteFile(file.FullPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispatch file {FilePath} for job {JobId}", file.FullPath, job.Id);
            return false;
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete file {FilePath} after dispatch", path);
        }
    }
}
