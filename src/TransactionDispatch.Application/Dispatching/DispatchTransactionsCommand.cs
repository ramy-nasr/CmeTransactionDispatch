namespace TransactionDispatch.Application.Dispatching;

public sealed record DispatchTransactionsCommand(
    string FolderPath,
    bool DeleteAfterSend,
    IReadOnlyCollection<string>? AllowedExtensions = null,
    string? IdempotencyKey = null
);
