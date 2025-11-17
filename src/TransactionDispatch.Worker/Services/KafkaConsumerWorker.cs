using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TransactionDispatch.Worker.Configuration;

namespace TransactionDispatch.Worker.Services;

public sealed class KafkaConsumerWorker(IOptions<KafkaConsumerOptions> options, ILogger<KafkaConsumerWorker> logger) : BackgroundService
{
    private readonly ILogger<KafkaConsumerWorker> _logger = logger;
    private readonly KafkaConsumerOptions _options = options.Value;
    private IConsumer<string, byte[]>? _consumer;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BootstrapServers))
        {
            throw new InvalidOperationException("Kafka consumer bootstrap servers must be configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.Topic))
        {
            throw new InvalidOperationException("Kafka consumer topic must be configured.");
        }

        var groupId = string.IsNullOrWhiteSpace(_options.GroupId)
            ? $"transaction-dispatch-worker-{Environment.MachineName}"
            : _options.GroupId;

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = groupId,
            EnableAutoCommit = _options.EnableAutoCommit,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            FetchMaxBytes = _options.FetchMaxBytes > 0 ? _options.FetchMaxBytes : null
        };

        var consumer = new ConsumerBuilder<string, byte[]>(config)
            .SetErrorHandler((_, error) =>
                _logger.LogError("Kafka consumer error: {Reason}", error.Reason))
            .Build();

        _consumer = consumer;
        consumer.Subscribe(_options.Topic);

        _logger.LogInformation("Kafka consumer subscribed to {Topic}", _options.Topic);

        var maxDegreeOfParallelism = Math.Max(1, _options.MaxDegreeOfParallelism);
        var inFlight = new List<Task<ProcessingOutcome>>(maxDegreeOfParallelism);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (inFlight.Count >= maxDegreeOfParallelism)
                {
                    await AwaitAndHandleCompletionAsync(inFlight, consumer, stoppingToken);
                }

                ConsumeResult<string, byte[]>? result = null;

                try
                {
                    result = consumer.Consume(TimeSpan.FromMilliseconds(200));
                }
                catch (ConsumeException ex)
                {
                    if (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Kafka consume error");
                    }
                }

                if (result is null)
                {
                    await DrainCompletedAsync(inFlight, consumer, stoppingToken);
                    continue;
                }

                var processingTask = ProcessMessageAsync(result, stoppingToken);
                inFlight.Add(processingTask);

                await DrainCompletedAsync(inFlight, consumer, stoppingToken);
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogInformation("Kafka consumer canceled: {Message}", ex.Message);
        }
        finally
        {
            while (inFlight.Count > 0)
            {
                await AwaitAndHandleCompletionAsync(inFlight, consumer, CancellationToken.None);
            }

            consumer.Close();
            consumer.Dispose();

            _logger.LogInformation("Kafka consumer closed");
        }
    }

    private async Task<ProcessingOutcome> ProcessMessageAsync(
        ConsumeResult<string, byte[]> result,
        CancellationToken cancellationToken)
    {
        try
        {
            await ExecuteWithRetryAsync(
                () => HandleMessageAsync(result, cancellationToken),
                cancellationToken);

            return new ProcessingOutcome(result, null);
        }
        catch (OperationCanceledException)
        {
            return new ProcessingOutcome(result, null);
        }
        catch (Exception ex)
        {
            return new ProcessingOutcome(result, ex);
        }
    }

    private async Task AwaitAndHandleCompletionAsync(
        List<Task<ProcessingOutcome>> inFlight,
        IConsumer<string, byte[]> consumer,
        CancellationToken cancellationToken)
    {
        if (inFlight.Count == 0)
        {
            return;
        }

        var completed = await Task.WhenAny(inFlight);
        inFlight.Remove(completed);

        var outcome = await completed;
        await HandleCompletionAsync(outcome, consumer, cancellationToken);
    }

    private async Task DrainCompletedAsync(
        List<Task<ProcessingOutcome>> inFlight,
        IConsumer<string, byte[]> consumer,
        CancellationToken cancellationToken)
    {
        for (var i = inFlight.Count - 1; i >= 0; i--)
        {
            var task = inFlight[i];

            if (!task.IsCompleted)
            {
                continue;
            }

            inFlight.RemoveAt(i);
            var outcome = await task;
            await HandleCompletionAsync(outcome, consumer, cancellationToken);
        }
    }

    private async Task ExecuteWithRetryAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        var maxAttempts = Math.Max(1, _options.MaxProcessingRetries);
        var baseDelayMs = Math.Max(0, _options.RetryBackoffMilliseconds);

        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await action();
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;

                if (attempt >= maxAttempts)
                {
                    throw;
                }

                var delay = CalculateBackoffDelay(baseDelayMs, attempt - 1);

                if (delay > TimeSpan.Zero)
                {
                    _logger.LogWarning(
                        ex,
                        "Processing attempt {Attempt}/{MaxAttempts} failed. Retrying in {Delay}...",
                        attempt,
                        maxAttempts,
                        delay);

                    try
                    {
                        await Task.Delay(delay, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                }
                else
                {
                    _logger.LogWarning(
                        ex,
                        "Processing attempt {Attempt}/{MaxAttempts} failed. Retrying immediately...",
                        attempt,
                        maxAttempts);
                }
            }
        }

        if (lastException is not null)
        {
            throw lastException;
        }
    }

    private static TimeSpan CalculateBackoffDelay(int baseDelayMs, int retryNumber)
    {
        if (baseDelayMs <= 0)
        {
            return TimeSpan.Zero;
        }

        var multiplier = Math.Pow(2, Math.Max(0, retryNumber));
        var delayMs = baseDelayMs * multiplier;

        return TimeSpan.FromMilliseconds(Math.Min(delayMs, int.MaxValue));
    }

    private async Task HandleCompletionAsync(
        ProcessingOutcome outcome,
        IConsumer<string, byte[]> consumer,
        CancellationToken cancellationToken)
    {
        if (outcome.Exception is not null)
        {
            _logger.LogError(
                outcome.Exception,
                "Error while processing message from partition {Partition} at offset {Offset}",
                outcome.Result.Partition,
                outcome.Result.Offset);
            return;
        }

        if (_options.EnableAutoCommit)
        {
            return;
        }

        try
        {
            consumer.Commit(outcome.Result);
        }
        catch (KafkaException ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Failed to commit Kafka message offset");
            }
        }
    }

    private Task HandleMessageAsync(
        ConsumeResult<string, byte[]> result,
        CancellationToken cancellationToken)
    {
        LogMessage(result);
        return Task.CompletedTask;
    }

    private void LogMessage(ConsumeResult<string, byte[]> result)
    {
        var payloadSize = result.Message.Value?.Length ?? 0;

        _logger.LogInformation(
            "Consumed message with key {Key} from partition {Partition} at offset {Offset} ({Bytes} bytes)",
            result.Message.Key,
            result.Partition,
            result.Offset,
            payloadSize);
    }

    private sealed record ProcessingOutcome(
        ConsumeResult<string, byte[]> Result,
        Exception? Exception);

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _consumer?.Close();
        _consumer?.Dispose();

        return base.StopAsync(cancellationToken);
    }
}
