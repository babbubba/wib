using Microsoft.Extensions.DependencyInjection;
using WIB.Application.Interfaces;

namespace WIB.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IReceiptQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;

    public Worker(ILogger<Worker> logger, IReceiptQueue queue, IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _queue = queue;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker started");
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

                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<ReceiptProcessor>();

                _logger.LogInformation("Processing receipt object {ObjectKey} for user {UserId}", queueItem.ObjectKey, queueItem.UserId);
                await processor.ProcessAsync(queueItem.ObjectKey, queueItem.UserId, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing queue item {ObjectKey}", queueItem?.ObjectKey ?? "<null>");
                if (queueItem is not null)
                {
                    try
                    {
                        await _queue.EnqueueAsync(queueItem, CancellationToken.None);
                    }
                    catch (Exception enqueueEx)
                    {
                        _logger.LogError(enqueueEx, "Failed to requeue item {ObjectKey}", queueItem.ObjectKey);
                    }
                }
                await Task.Delay(1000, stoppingToken);
            }
        }

        _logger.LogInformation("Worker stopping");
    }
}
