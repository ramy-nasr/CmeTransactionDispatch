using TransactionDispatch.Domain;

namespace TransactionDispatch.Application.Dispatching;

public interface IDispatchApplicationService
{
    Task<DispatchJobId> DispatchTransactionsAsync(DispatchTransactionsCommand command, CancellationToken cancellationToken);

    Task<DispatchJobSnapshot?> GetJobStatusAsync(DispatchJobId jobId, CancellationToken cancellationToken);
}
