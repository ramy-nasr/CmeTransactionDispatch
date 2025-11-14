using System.Linq;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using TransactionDispatch.Api.Options;
using TransactionDispatch.Application.Dispatching;
using TransactionDispatch.Domain;
using TransactionDispatch.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

builder.Services.AddTransactionDispatchInfrastructure(builder.Configuration);

builder.Services
    .AddOptions<DispatchConfigurationOptions>()
    .Bind(builder.Configuration.GetSection("Dispatch"))
    .Validate(options => options.AllowedExtensions is null || options.AllowedExtensions.All(e => !string.IsNullOrWhiteSpace(e)),
        "AllowedExtensions entries must be non-empty when configured.")
    .ValidateOnStart();

builder.Services.AddSingleton<IDispatchApplicationService, DispatchApplicationService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health");

app.MapPost("/dispatch-transactions", async (
    DispatchRequest request,
    IDispatchApplicationService service,
    IOptions<DispatchConfigurationOptions> dispatchOptions,
    CancellationToken cancellationToken) =>
{
    var configuredExtensions = dispatchOptions.Value.AllowedExtensions ?? new List<string>();
    IReadOnlyCollection<string> allowedExtensions = (request.AllowedExtensions?.Count ?? 0) > 0
        ? request.AllowedExtensions.ToArray()
        : configuredExtensions.ToArray();

    var command = new DispatchTransactionsCommand(
        request.FolderPath,
        request.DeleteAfterSend,
        allowedExtensions,
        request.IdempotencyKey);
    var jobId = await service.DispatchTransactionsAsync(command, cancellationToken).ConfigureAwait(false);
    return Results.Accepted($"/dispatch-status/{jobId}", new { jobId });
})
.WithName("DispatchTransactions");

app.MapGet("/dispatch-status/{jobId:guid}", async (
    Guid jobId,
    IDispatchApplicationService service,
    CancellationToken cancellationToken) =>
{
    var snapshot = await service.GetJobStatusAsync(new(jobId), cancellationToken).ConfigureAwait(false);
    if (snapshot is null)
    {
        return Results.NotFound();
    }

    var progress = snapshot.Progress;
    return Results.Ok(new DispatchStatusResponse(
        snapshot.Id.Value,
        snapshot.Status.ToString(),
        $"{progress.Percentage:0.##}%",
        progress.TotalFiles,
        progress.Processed,
        progress.Succeeded,
        progress.Failed,
        snapshot.CompletedAt,
        snapshot.FailureReason));
})
.WithName("GetDispatchStatus");

app.Run();

internal sealed record DispatchRequest(
    string FolderPath,
    bool DeleteAfterSend,
    IReadOnlyCollection<string>? AllowedExtensions,
    string? IdempotencyKey
);

internal sealed record DispatchStatusResponse(
    Guid JobId,
    string Status,
    string Progress,
    int TotalFiles,
    int Processed,
    int Successful,
    int Failed,
    DateTimeOffset? CompletedAt,
    string? FailureReason
);
