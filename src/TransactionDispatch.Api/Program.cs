using Microsoft.Extensions.Options;
using TransactionDispatch.Api.Options;
using TransactionDispatch.Application.Dispatching;
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

builder.Services.AddSingleton<ILogger>(sp =>
{
    var factory = sp.GetRequiredService<ILoggerFactory>();
    return factory.CreateLogger("Default");
});

builder.Services.AddScoped<IDispatchApplicationService, DispatchApplicationService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health");

app.MapPost("/dispatch-transactions", static async (
    DispatchRequest request,
    IDispatchApplicationService service,
    IOptions<DispatchConfigurationOptions> dispatchOptions,
    ILogger logger,
    CancellationToken cancellationToken) =>
{
    try
    {
        if (request == null)
            return Results.BadRequest();


        var configuredExtensions = dispatchOptions.Value.AllowedExtensions ?? new List<string>();

        var command = new DispatchTransactionsCommand(
            request.FolderPath,
            request.DeleteAfterSend,
            request.IdempotencyKey);
        var jobId = await service.DispatchTransactionsAsync(command, configuredExtensions, cancellationToken).ConfigureAwait(false);
        return Results.Accepted($"/dispatch-status/{jobId}", new { jobId });
    }
    catch (Exception ex)
    {
        logger.LogError("Error occurred while dispatching transactions: {ErrorMessage}", ex.Message);
        return Results.BadRequest();
    }
})
.WithName("DispatchTransactions")
.WithOpenApi();

app.MapGet("/dispatch-status/{jobId:guid}", async (
    Guid jobId,
    IDispatchApplicationService service,
    ILogger logger,
    CancellationToken cancellationToken) =>
{
    try
    {
        if (jobId == Guid.Empty)
            return Results.BadRequest();


        var snapshot = await service.GetJobStatusAsync(new(jobId), cancellationToken).ConfigureAwait(false);
        if (snapshot is null)
            return Results.NotFound();


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
    }
    catch (Exception ex)
    {
        logger.LogError("Error occurred while dispatching transactions: {ErrorMessage}", ex.Message);
        return Results.BadRequest();
    }
})
.WithName("GetDispatchStatus")
.WithOpenApi();

app.Run();

internal sealed record DispatchRequest(
    string FolderPath,
    bool DeleteAfterSend,
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
