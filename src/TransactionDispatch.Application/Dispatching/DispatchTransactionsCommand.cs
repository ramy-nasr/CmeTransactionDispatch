namespace TransactionDispatch.Application.Dispatching;

public sealed record DispatchTransactionsCommand(
    string FolderPath,
    bool DeleteAfterSend,
    string? IdempotencyKey = null
);
