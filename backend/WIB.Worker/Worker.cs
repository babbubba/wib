using WIB.Application.Interfaces;

namespace WIB.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IReceiptQueue _queue;
    private readonly ReceiptProcessor _processor;

    public Worker(ILogger<Worker> logger, IReceiptQueue queue, ReceiptProcessor processor)
    {
        _logger = logger;
        _queue = queue;
        _processor = processor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var key = await _queue.TryDequeueAsync(stoppingToken);
                if (key is null)
                {
                    await Task.Delay(500, stoppingToken);
                    continue;
                }

                _logger.LogInformation("Processing receipt object {ObjectKey}", key);
                await _processor.ProcessAsync(key, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing queue item");
                await Task.Delay(1000, stoppingToken);
            }
        }
        _logger.LogInformation("Worker stopping");
    }
}
