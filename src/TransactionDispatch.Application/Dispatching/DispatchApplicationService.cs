using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.IO;
using TransactionDispatch.Domain;
using TransactionDispatch.Domain.Abstractions;
using TransactionDispatch.Infrastructure.Messaging;

namespace TransactionDispatch.Application.Dispatching;

public sealed class DispatchApplicationService : IDispatchApplicationService
{
    private const string JobMessageContentType = "application/vnd.transaction-dispatch.job+json";
    private static readonly JsonSerializerOptions JobPayloadSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IDispatchJobRepository _repository;
    private readonly IKafkaProducer _kafkaProducer;
    private readonly ILogger<DispatchApplicationService> _logger;

    public DispatchApplicationService(
        IDispatchJobRepository repository,
        IKafkaProducer kafkaProducer,
        ILogger<DispatchApplicationService> logger)
    {
        _repository = repository;
        _kafkaProducer = kafkaProducer;
        _logger = logger;
    }

    public async Task<DispatchJobId> DispatchTransactionsAsync(
        DispatchTransactionsCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var folderPath = NormalizeFolderPath(command.FolderPath);
        var normalizedIdempotencyKey = NormalizeIdempotencyKey(command.IdempotencyKey);
        if (normalizedIdempotencyKey is not null)
        {
            var existingJob = await _repository
                .GetByIdempotencyKeyAsync(normalizedIdempotencyKey, cancellationToken)
                .ConfigureAwait(false);

            if (existingJob is not null)
            {
                _logger.LogInformation(
                    "Dispatch job {JobId} already exists for idempotency key {IdempotencyKey}",
                    existingJob.Id,
                    normalizedIdempotencyKey);
                return existingJob.Id;
            }
        }

        var job = CreateJob(command, folderPath, normalizedIdempotencyKey);
        var added = await _repository.AddAsync(job, cancellationToken).ConfigureAwait(false);
        if (!added)
        {
            var conflictingJobId = await ResolveConflictingJobIdAsync(job, cancellationToken).ConfigureAwait(false);
            if (conflictingJobId is null)
            {
                throw new InvalidOperationException($"Failed to add dispatch job {job.Id}.");
            }

            return conflictingJobId;
        }

        await PublishJobQueuedAsync(job, cancellationToken).ConfigureAwait(false);
        return job.Id;
    }

    public Task<DispatchJobSnapshot?> GetJobStatusAsync(
        DispatchJobId jobId,
        CancellationToken cancellationToken)
    {
        return _repository.GetSnapshotAsync(jobId, cancellationToken);
    }

    private static DispatchJob CreateJob(
        DispatchTransactionsCommand command,
        string normalizedFolderPath,
        string? idempotencyKey)
    {
        return new DispatchJob(
            DispatchJobId.NewId(),
            normalizedFolderPath,
            command.DeleteAfterSend,
            NormalizeAllowedExtensions(command.AllowedExtensions),
            idempotencyKey);
    }

    private static string NormalizeFolderPath(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath, nameof(folderPath));

        var trimmed = folderPath.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Folder path is required", nameof(folderPath));
        }

        return Path.EndsInDirectorySeparator(trimmed)
            ? trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            : trimmed;
    }

    private static string? NormalizeIdempotencyKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        return key.Trim();
    }

    private static IReadOnlyCollection<string> NormalizeAllowedExtensions(IReadOnlyCollection<string>? extensions)
    {
        if (extensions is null || extensions.Count == 0)
        {
            return Array.Empty<string>();
        }

        var normalized = extensions
            .Select(extension => extension?.Trim())
            .Where(extension => !string.IsNullOrEmpty(extension))
            .Select(extension => extension![0] == '.' ? extension : $".{extension}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length == 0 ? Array.Empty<string>() : normalized;
    }

    private async Task<DispatchJobId?> ResolveConflictingJobIdAsync(
        DispatchJob candidate,
        CancellationToken cancellationToken)
    {
        if (candidate.IdempotencyKey is not null)
        {
            var idempotentJob = await _repository
                .GetByIdempotencyKeyAsync(candidate.IdempotencyKey, cancellationToken)
                .ConfigureAwait(false);

            if (idempotentJob is not null)
            {
                return idempotentJob.Id;
            }
        }

        var snapshot = await _repository.GetSnapshotAsync(candidate.Id, cancellationToken).ConfigureAwait(false);
        return snapshot?.Id;
    }

    private async Task PublishJobQueuedAsync(DispatchJob job, CancellationToken cancellationToken)
    {
        try
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(
                new DispatchJobQueuedPayload(
                    job.Id.Value,
                    job.FolderPath,
                    job.DeleteAfterSend,
                    job.AllowedExtensions,
                    job.IdempotencyKey),
                JobPayloadSerializerOptions);

            var headers = new Dictionary<string, string>
            {
                ["job-id"] = job.Id.ToString()
            };

            if (!string.IsNullOrWhiteSpace(job.IdempotencyKey))
            {
                headers["idempotency-key"] = job.IdempotencyKey!;
            }

            var message = new TransactionMessage(
                job.Id.ToString(),
                payload,
                JobMessageContentType,
                headers);

            await _kafkaProducer.ProduceAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue dispatch job {JobId}", job.Id);
            throw;
        }
    }

    private sealed record DispatchJobQueuedPayload(
        Guid JobId,
        string FolderPath,
        bool DeleteAfterSend,
        IReadOnlyCollection<string> AllowedExtensions,
        string? IdempotencyKey);
}
