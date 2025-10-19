using Microsoft.Extensions.DependencyInjection;
using WIB.Application.Interfaces;

namespace WIB.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IRedisLogger _redisLogger;
    private readonly IReceiptQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;

    public Worker(ILogger<Worker> logger, IRedisLogger redisLogger, IReceiptQueue queue, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _redisLogger = redisLogger;
        _queue = queue;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker started");
        await _redisLogger.InfoAsync("Worker Started", "Receipt processing worker has started successfully");

        while (!stoppingToken.IsCancellationRequested)
        {
            ReceiptQueueItem? queueItem = null;
            try
            {
                queueItem = await _queue.TryDequeueAsync(stoppingToken);
                if (queueItem is null)
                {
                    await Task.Delay(500, stoppingToken);
                    continue;
                }

                _logger.LogInformation("Processing receipt object {ObjectKey} for user {UserId}", queueItem.ObjectKey, queueItem.UserId);
                await _redisLogger.InfoAsync(
                    "Receipt Dequeued",
                    $"Starting to process receipt: {queueItem.ObjectKey}",
                    new Dictionary<string, object>
                    {
                        ["objectKey"] = queueItem.ObjectKey,
                        ["userId"] = queueItem.UserId.ToString()
                    });

                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<ReceiptProcessor>();

                await processor.ProcessAsync(queueItem.ObjectKey, queueItem.UserId, stoppingToken);

                await _redisLogger.InfoAsync(
                    "Receipt Processed",
                    $"Successfully processed receipt: {queueItem.ObjectKey}",
                    new Dictionary<string, object>
                    {
                        ["objectKey"] = queueItem.ObjectKey,
                        ["userId"] = queueItem.UserId.ToString()
                    });
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing queue item {ObjectKey}", queueItem?.ObjectKey ?? "<null>");
                await _redisLogger.ErrorAsync(
                    "Receipt Processing Failed",
                    $"Failed to process receipt: {queueItem?.ObjectKey ?? "<null>"}",
                    ex,
                    new Dictionary<string, object>
                    {
                        ["objectKey"] = queueItem?.ObjectKey ?? "<null>",
                        ["userId"] = queueItem?.UserId.ToString() ?? "<null>"
                    });

                if (queueItem is not null)
                {
                    try
                    {
                        await _queue.EnqueueAsync(queueItem, CancellationToken.None);
                        await _redisLogger.InfoAsync(
                            "Receipt Requeued",
                            $"Receipt {queueItem.ObjectKey} has been requeued for retry",
                            new Dictionary<string, object>
                            {
                                ["objectKey"] = queueItem.ObjectKey
                            });
                    }
                    catch (Exception enqueueEx)
                    {
                        _logger.LogError(enqueueEx, "Failed to requeue item {ObjectKey}", queueItem.ObjectKey);
                        await _redisLogger.ErrorAsync(
                            "Requeue Failed",
                            $"Failed to requeue receipt: {queueItem.ObjectKey}",
                            enqueueEx,
                            new Dictionary<string, object>
                            {
                                ["objectKey"] = queueItem.ObjectKey
                            });
                    }
                }
                await Task.Delay(1000, stoppingToken);
            }
        }

        _logger.LogInformation("Worker stopping");
        await _redisLogger.InfoAsync("Worker Stopping", "Receipt processing worker is shutting down");
    }
}
