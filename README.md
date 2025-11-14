# Transaction Dispatch Microservice

This solution provides two .NET 8 applications that collaborate to move transaction
files from disk into Kafka and fan the resulting messages out to downstream
consumers.

## Solution Layout

- **TransactionDispatch.Domain** – Domain model containing immutable job and
  message primitives plus the job lifecycle rules.
- **TransactionDispatch.Application** – Application layer exposing the dispatch
  service contract and job query logic. It coordinates the domain model while
  directly invoking the infrastructure adapters responsible for Kafka publishing
  and file discovery.
- **TransactionDispatch.Infrastructure** – Infrastructure adapters that publish to
  Kafka, enumerate files, and manage job state. It remains focused on external
  integrations without hosting background processing concerns.
- **TransactionDispatch.Api** – Minimal API that accepts dispatch requests,
  returns job identifiers immediately, and exposes job status endpoints. The API
  calls into the application layer, which begins dispatching the folder contents
  without relying on hosted background services.
- **TransactionDispatch.Worker** – Standalone background service that consumes
  Kafka messages without referencing the infrastructure project and processes
  messages concurrently according to its configured degree of parallelism.
- **TransactionDispatch.Tests** – Unit tests for the dispatch orchestration
  behavior.

## Dispatch Flow

1. Clients call `POST /dispatch-transactions` with a folder path, optional
   idempotency key, and file deletion flag.
2. The API forwards the request to the application service (or reuses the
   existing job when the idempotency key already exists) and returns the job
   identifier immediately.
3. The application service starts processing the job: it discovers matching files
   through the infrastructure file system adapter, publishes each file to Kafka
   through the producer adapter, and tracks progress and outcomes in the
   repository while optionally deleting files after successful sends.
4. Clients query `GET /dispatch-status/{jobId}` to retrieve progress details.

The background worker hosts a Kafka consumer that streams messages off the topic
so downstream logic can plug in custom handlers.

## Configuration

- `appsettings.json` in the API project controls dispatch behavior:
  - `Dispatch.AllowedExtensions` enumerates the extensions that are eligible for
    dispatch when the request omits overrides.
  - `Kafka:Producer` specifies the Kafka broker connection, topic, client id,
    acknowledgement strategy, batching, timeout, and compression settings used
    by the infrastructure producer adapter.
- The worker project keeps its own consumer configuration under
  `Kafka:Consumer` and does not depend on the infrastructure project. Options
  include `MaxDegreeOfParallelism` to cap concurrent message handlers alongside
  typical Kafka consumer settings such as bootstrap servers, topic, group id,
  and commit strategy.

Both applications rely on environment variables or `appsettings.*.json` files for
secret material such as Kafka credentials.
