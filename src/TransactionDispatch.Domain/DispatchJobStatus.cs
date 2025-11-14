namespace TransactionDispatch.Domain;

public enum DispatchJobStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}
