using Microsoft.AspNetCore.Server.Kestrel.Core;
using Serilog;
using System.Text.Json.Serialization;

namespace DockerProxy
{
    public class Program
    {
        public static void Main(string[] args)
        {
            try
            {
                var builder = WebApplication.CreateBuilder(args);

                // Add configuration from appsettings.json
                builder.Configuration.AddJsonFile("appsettings.json", optional: false);

                // Register configuration
                var cfg = builder.Configuration.GetSection("Registry");
                builder.Services.Configure<AppConfig>(cfg);

                // Load configuration
                var config = new AppConfig();
                cfg.Bind(config);

                // 从配置中读取 Serilog 设置
                builder.Host.UseSerilog((context, services, configuration) => configuration
                    .ReadFrom.Configuration(context.Configuration)
                    .ReadFrom.Services(services));

                Log.Information("Starting Docker Registry Mirror");

                // Add services to the container.
                // Configure Kestrel for better streaming performance
                builder.Services.Configure<KestrelServerOptions>(options =>
                {
                    options.Limits.MaxRequestBodySize = long.MaxValue; // Optional: remove request body size limit
                    options.AllowSynchronousIO = false;
                });

                // Register memory cache with size limits
                builder.Services.AddMemoryCache(options =>
                {
                    // Get memory limit from config or use default 128MB
                    int memoryLimitMB = builder.Configuration.GetValue<int>("Registry:MemoryLimit");
                    if (memoryLimitMB <= 0)
                        memoryLimitMB = 128;

                    options.SizeLimit = memoryLimitMB * 1024 * 1024; // Convert to bytes
                });

                // Register HTTP client factory
                builder.Services.AddHttpClient("DockerRegistry", client =>
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "Docker-Client/24.0.6 (linux)");
                    client.Timeout = TimeSpan.FromSeconds(60); // Longer timeout
                }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                    MaxConnectionsPerServer = 20,
                    EnableMultipleHttp2Connections = true,
                    AutomaticDecompression = System.Net.DecompressionMethods.All
                });

                builder.Services.AddSingleton<TokenService>();
                builder.Services.AddSingleton<DockerRegistryService>();

                builder.Services.AddHostedService<CacheCleanupService>();

                // Configure services
                builder.Services.AddControllers()
                    .AddJsonOptions(options =>
                    {
                        options.JsonSerializerOptions.PropertyNamingPolicy = null;
                        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
                    });

                // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
                builder.Services.AddEndpointsApiExplorer();
                builder.Services.AddSwaggerGen();

                var app = builder.Build();

                // Create cache directories
                EnsureDirectoryExists(config.CacheDir);
                EnsureDirectoryExists(Path.Combine(config.CacheDir, "manifests"));
                EnsureDirectoryExists(Path.Combine(config.CacheDir, "blobs"));

                // 添加 Serilog 请求日志中间件
                app.UseSerilogRequestLogging();

                // Configure the HTTP request pipeline.
                if (app.Environment.IsDevelopment())
                {
                    app.UseSwagger();
                    app.UseSwaggerUI();
                }

                app.UseAuthorization();

                app.MapControllers();

                // Configure pipeline
                //app.UseMiddleware<ErrorHandlingMiddleware>();

                // Start the application
                Log.Information($"Cache directory: {config.CacheDir}");
                Log.Information($"Memory limit: {config.MemoryLimit} MB");

                app.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application startup failed");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        private static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                Log.Information("Created directory: {Path}", path);
            }
        }
    }
}