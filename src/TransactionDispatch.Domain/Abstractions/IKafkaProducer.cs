namespace TransactionDispatch.Domain.Messaging;

using TransactionDispatch.Domain;

public interface IKafkaProducer
{
    Task ProduceAsync(TransactionMessage message, CancellationToken cancellationToken);
}