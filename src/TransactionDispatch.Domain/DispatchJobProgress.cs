namespace TransactionDispatch.Domain;

public sealed record DispatchJobProgress(int TotalFiles, int Processed, int Succeeded, int Failed)
{
    public decimal Percentage => TotalFiles == 0 ? 0 : Math.Round(Processed / (decimal)TotalFiles * 100, 2);

    public DispatchJobProgress IncrementTotal(int amount = 1) => this with { TotalFiles = TotalFiles + amount };

    public DispatchJobProgress IncrementProcessed(bool success)
    {
        var succeeded = Succeeded + (success ? 1 : 0);
        var failed = Failed + (success ? 0 : 1);
        return this with { Processed = Processed + 1, Succeeded = succeeded, Failed = failed };
    }
}
