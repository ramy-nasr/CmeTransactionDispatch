using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TransactionDispatch.Application.Dispatching;
using TransactionDispatch.Domain;
using TransactionDispatch.Domain.Abstractions;
using TransactionDispatch.Domain.FileSystem;
using TransactionDispatch.Domain.Messaging;
using Xunit;

namespace TransactionDispatch.Tests;

public class DispatchApplicationServiceTests
{
    private readonly Mock<IDispatchJobRepository> _repository = new();
    private readonly Mock<IFileDiscoveryService> _fileDiscovery = new();
    private readonly Mock<IKafkaProducer> _producer = new();

    [Fact]
    public async Task DispatchTransactionsAsync_CreatesJobAndProcessesFiles()
    {
        DispatchJob? storedJob = null;
        var discoveryCalled = new TaskCompletionSource<bool>();

        _repository
            .Setup(r => r.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DispatchJob?)null);

        _repository
            .Setup(r => r.AddAsync(It.IsAny<DispatchJob>(), It.IsAny<CancellationToken>()))
            .Callback<DispatchJob, CancellationToken>((job, _) => storedJob = job)
            .ReturnsAsync(true);

        _repository
            .Setup(r => r.GetAsync(It.IsAny<DispatchJobId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => storedJob);

        _repository
            .Setup(r => r.UpdateAsync(It.IsAny<DispatchJob>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repository
            .Setup(r => r.GetSnapshotAsync(It.IsAny<DispatchJobId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DispatchJobSnapshot?)null);

        _fileDiscovery
            .Setup(f => f.FindFilesAsync(It.IsAny<DispatchJob>(), It.IsAny<CancellationToken>()))
            .Returns<DispatchJob, CancellationToken>((_, _) =>
            {
                discoveryCalled.TrySetResult(true);
                return Task.FromResult<IReadOnlyCollection<FileEntry>>(Array.Empty<FileEntry>());
            });

        _producer
            .Setup(p => p.ProduceAsync(It.IsAny<TransactionMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = new DispatchApplicationService(
            _repository.Object,
            _fileDiscovery.Object,
            _producer.Object,
            NullLogger<DispatchApplicationService>.Instance);

        var command = new DispatchTransactionsCommand("/tmp", true, null);
        var jobId = await sut.DispatchTransactionsAsync(command, [".xml"], CancellationToken.None);

        jobId.Value.Should().NotBe(Guid.Empty);
        storedJob.Should().NotBeNull();
        await discoveryCalled.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task DispatchTransactionsAsync_InvalidFolder_Throws()
    {
        var sut = new DispatchApplicationService(
            _repository.Object,
            _fileDiscovery.Object,
            _producer.Object,
            NullLogger<DispatchApplicationService>.Instance);

        var command = new DispatchTransactionsCommand(string.Empty, false, null);

        await Assert.ThrowsAsync<ArgumentException>(() => sut.DispatchTransactionsAsync(command, [".xml"], CancellationToken.None));
    }

    [Fact]
    public async Task DispatchTransactionsAsync_WithExistingIdempotencyKey_ReturnsExistingJobId()
    {
        var existingJobId = DispatchJobId.NewId();
        var existingJob = new DispatchJob(existingJobId, "/tmp", true, new[] { ".xml" }, "abc");

        _repository
            .Setup(r => r.GetByIdempotencyKeyAsync("abc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingJob);

        var sut = new DispatchApplicationService(
            _repository.Object,
            _fileDiscovery.Object,
            _producer.Object,
            NullLogger<DispatchApplicationService>.Instance);

        var command = new DispatchTransactionsCommand("/tmp", true, "abc");
        var jobId = await sut.DispatchTransactionsAsync(command, [".xml"], CancellationToken.None);

        jobId.Should().Be(existingJobId);
        _repository.Verify(r => r.AddAsync(It.IsAny<DispatchJob>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
