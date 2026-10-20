using ZenithFin.PostgreSQL.Models.Services;

namespace ZenithFin.Api
{
    public sealed class TransactionSyncJob : BackgroundService
    {
        private readonly TransactionService _transactionService;
        private readonly TimeSpan _interval;

        private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

        public TransactionSyncJob(TransactionService transactionService,
                                  IConfiguration configuration)
        {
            _transactionService = transactionService;
            _interval = TimeSpan.FromHours(configuration.GetValue("Sync:BackgroundIntervalHours", 6.0));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_interval <= TimeSpan.Zero) return;

            try
            {
                await Task.Delay(StartupDelay, stoppingToken);

                using PeriodicTimer timer = new(CheckInterval);

                do
                {
                    try
                    {
                        int synced = await _transactionService.SyncInBackgroundAsync(_interval);

                        if (synced > 0) Console.WriteLine($"Background sync: {synced} accounts updated");
                    }
                    catch (Exception errno)
                    {
                        Console.WriteLine($"Background sync failed: {errno.Message}");
                    }
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
