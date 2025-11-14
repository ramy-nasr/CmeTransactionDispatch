namespace TransactionDispatch.Domain;

public readonly record struct DispatchJobId(Guid Value)
{
    public static DispatchJobId NewId() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
