using TransactionDispatch.Domain;

namespace TransactionDispatch.Application.Dispatching;

public interface IDispatchApplicationService
{
    Task<DispatchJobId> DispatchTransactionsAsync(DispatchTransactionsCommand command, List<string> options, CancellationToken cancellationToken);

    Task<DispatchJobSnapshot?> GetJobStatusAsync(DispatchJobId jobId, CancellationToken cancellationToken);
}