namespace TransactionDispatch.Domain;

public sealed record TransactionMessage(
    string FileName,
    byte[] Payload,
    string ContentType,
    IDictionary<string, string>? Headers = null
);
