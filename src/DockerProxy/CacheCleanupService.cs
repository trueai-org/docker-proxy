using Microsoft.Extensions.Options;
using Serilog;

namespace DockerProxy
{
    public class CacheCleanupService : BackgroundService
    {
        private readonly AppConfig _config;
        private readonly TimeSpan _cleanupInterval = TimeSpan.FromHours(1);

        public CacheCleanupService(IOptions<AppConfig> config)
        {
            _config = config.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Log.Information("Cache cleanup service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_cleanupInterval, stoppingToken);

                    if (stoppingToken.IsCancellationRequested)
                        break;

                    Log.Information("Starting cache cleanup");

                    // Get cache directory info
                    var cacheDir = new DirectoryInfo(_config.CacheDir);
                    if (!cacheDir.Exists)
                    {
                        Log.Warning("Cache directory does not exist: {CacheDir}", _config.CacheDir);
                        continue;
                    }

                    // Calculate expiration threshold
                    var expirationThreshold = DateTime.UtcNow.AddSeconds(-_config.CacheTTL);

                    // Get all cache files
                    var metadataFiles = cacheDir.GetFiles("*.*", SearchOption.AllDirectories);
                    Log.Information("Found {Count} cache items to check", metadataFiles.Length);

                    int removedCount = 0;
                    long freedSpace = 0;

                    foreach (var metaFile in metadataFiles)
                    {
                        try
                        {
                            if (metaFile.LastWriteTimeUtc < expirationThreshold)
                            {
                                // Get corresponding data file
                                var dataFile = new FileInfo(metaFile.FullName.Substring(0, metaFile.FullName.Length - 5));

                                // Delete files if they exist
                                if (dataFile.Exists)
                                {
                                    freedSpace += dataFile.Length;
                                    dataFile.Delete();
                                }

                                metaFile.Delete();
                                removedCount++;

                                // Yield every 100 files to prevent blocking
                                if (removedCount % 100 == 0)
                                {
                                    await Task.Yield();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Error cleaning up cache file: {File}", metaFile.FullName);
                        }
                    }

                    Log.Information("Cache cleanup completed. Removed {Count} expired items, freed {Space} MB",
                        removedCount, Math.Round(freedSpace / (1024.0 * 1024.0), 2));
                }
                catch (OperationCanceledException)
                {
                    // Normal during shutdown
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error during cache cleanup");

                    // Wait a bit before retrying
                    await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
                }
            }

            Log.Information("Cache cleanup service stopped");
        }
    }
}
