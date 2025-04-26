using DockerProxy;
using Microsoft.Extensions.Options;
using Serilog;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace DockerProxy
{
    public class DockerRegistryService
    {
        private readonly TokenService _tokenService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly AppConfig _config;
        private readonly SemaphoreSlim _concurrencySemaphore;

        public DockerRegistryService(TokenService tokenService, IHttpClientFactory httpClientFactory, IOptions<AppConfig> config)
        {
            _tokenService = tokenService;
            _httpClientFactory = httpClientFactory;
            _config = config.Value;
            _concurrencySemaphore = new SemaphoreSlim(4, 4); // Allow 4 concurrent requests
        }

        public async Task<RegistryResponse> GetManifestAsync(string repository, string reference)
        {
            string scope = $"repository:{repository}:pull";
            string url = $"https://registry-1.docker.io/v2/{repository}/manifests/{reference}";
            string cacheKey = Path.Combine(_config.CacheDir, "manifests", GetSafeFileName($"{repository}_{reference}"));

            // Check cache
            if (TryGetFromCache(cacheKey, out RegistryResponse cachedResponse))
            {
                Log.Information("Serving manifest from cache: {Repository}:{Reference}", repository, reference);
                return cachedResponse;
            }

            // Get token
            string token = await _tokenService.GetTokenAsync(scope);

            if (string.IsNullOrEmpty(token))
            {
                Log.Error("Failed to get token for {Repository}", repository);
                return new RegistryResponse
                {
                    StatusCode = 401,
                    ContentType = "application/json",
                    Content = new MemoryStream(Encoding.UTF8.GetBytes(
                        "{\"errors\":[{\"code\":\"UNAUTHORIZED\",\"message\":\"Failed to get authorization token\"}]}"
                    ))
                };
            }

            // Limit concurrent requests
            await _concurrencySemaphore.WaitAsync();

            try
            {
                Log.Information("Fetching manifest: {Repository}:{Reference}", repository, reference);

                // DockerRegistry
                var client = _httpClientFactory.CreateClient("registry");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                // Set Accept headers for manifest types
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.docker.distribution.manifest.v2+json"));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.docker.distribution.manifest.list.v2+json"));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.oci.image.manifest.v1+json"));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.oci.image.index.v1+json"));

                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("Failed to get manifest: {StatusCode} - {ErrorContent}",
                        response.StatusCode, errorContent);

                    return new RegistryResponse
                    {
                        StatusCode = (int)response.StatusCode,
                        ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json",
                        Content = new MemoryStream(Encoding.UTF8.GetBytes(errorContent))
                    };
                }

                // Read content
                var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/vnd.docker.distribution.manifest.v2+json";
                var stream = await response.Content.ReadAsStreamAsync();

                // IMPORTANT: Copy to a memory stream to avoid disposed stream issues
                var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                // Make a copy for caching
                var cacheStream = new MemoryStream();
                memoryStream.CopyTo(cacheStream);
                memoryStream.Position = 0;
                cacheStream.Position = 0;

                var result = new RegistryResponse
                {
                    StatusCode = (int)response.StatusCode,
                    ContentType = contentType,
                    Content = memoryStream
                };

                // Cache in the background
                _ = Task.Run(() => SaveToCache(cacheKey, contentType, cacheStream));

                return result;
            }
            catch (TaskCanceledException)
            {
                Log.Error("Request timed out for manifest: {Repository}:{Reference}", repository, reference);

                // Return a timeout error in proper Docker format
                return new RegistryResponse
                {
                    StatusCode = 408, // Request Timeout
                    ContentType = "application/json",
                    Content = new MemoryStream(Encoding.UTF8.GetBytes(
                        $"{{\"errors\":[{{\"code\":\"MANIFEST_TIMEOUT\",\"message\":\"Request for {repository}:{reference} timed out\"}}]}}"
                    ))
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting manifest for {Repository}:{Reference}", repository, reference);

                return new RegistryResponse
                {
                    StatusCode = 500,
                    ContentType = "application/json",
                    Content = new MemoryStream(Encoding.UTF8.GetBytes(
                        $"{{\"errors\":[{{\"code\":\"INTERNAL_ERROR\",\"message\":\"{ex.Message.Replace("\"", "\\\"")}\"}}]}}"
                    ))
                };
            }
            finally
            {
                _concurrencySemaphore.Release();
            }
        }

        public async Task<RegistryResponse> GetBlobAsync(string repository, string digest)
        {
            string scope = $"repository:{repository}:pull";
            string url = $"https://registry-1.docker.io/v2/{repository}/blobs/{digest}";
            string cacheKey = Path.Combine(_config.CacheDir, "blobs", GetSafeFileName($"{repository}_{digest}"));

            // Check cache
            if (TryGetFromCache(cacheKey, out RegistryResponse cachedResponse))
            {
                Log.Information("Serving blob from cache: {Repository}:{Digest}", repository, digest);
                return cachedResponse;
            }

            // Get token
            string token = await _tokenService.GetTokenAsync(scope);

            if (string.IsNullOrEmpty(token))
            {
                Log.Error("Failed to get token for blob: {Repository}", repository);
                return new RegistryResponse
                {
                    StatusCode = 401,
                    ContentType = "application/json",
                    Content = new MemoryStream(Encoding.UTF8.GetBytes(
                        "{\"errors\":[{\"code\":\"UNAUTHORIZED\",\"message\":\"Failed to get authorization token\"}]}"
                    ))
                };
            }

            // Limit concurrent requests
            await _concurrencySemaphore.WaitAsync();

            try
            {
                Log.Information("Fetching blob: {Repository}:{Digest}", repository, digest);

                // DockerRegistry
                var client = _httpClientFactory.CreateClient("registry");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("Failed to get blob: {StatusCode} - {ErrorContent}",
                        response.StatusCode, errorContent);

                    return new RegistryResponse
                    {
                        StatusCode = (int)response.StatusCode,
                        ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json",
                        Content = new MemoryStream(Encoding.UTF8.GetBytes(errorContent))
                    };
                }

                // For blobs, stream directly to file cache and then return a FileStream
                string contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";

                // Create a file stream and copy the content
                EnsureDirectoryExists(Path.GetDirectoryName(cacheKey));

                using (var fileStream = new FileStream(cacheKey, FileMode.Create, FileAccess.Write, FileShare.None,
                                                     _config.BufferSize, true))
                using (var contentStream = await response.Content.ReadAsStreamAsync())
                {
                    await contentStream.CopyToAsync(fileStream, _config.BufferSize);
                    await fileStream.FlushAsync();
                }

                // Save content type
                await File.WriteAllTextAsync($"{cacheKey}.type", contentType);

                // Return a fresh read stream
                var blobStream = new FileStream(cacheKey, FileMode.Open, FileAccess.Read, FileShare.Read,
                                               _config.BufferSize, FileOptions.Asynchronous);

                return new RegistryResponse
                {
                    StatusCode = 200,
                    ContentType = contentType,
                    Content = blobStream,
                    IsFileStream = true
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting blob for {Repository}:{Digest}", repository, digest);

                return new RegistryResponse
                {
                    StatusCode = 500,
                    ContentType = "application/json",
                    Content = new MemoryStream(Encoding.UTF8.GetBytes(
                        $"{{\"errors\":[{{\"code\":\"INTERNAL_ERROR\",\"message\":\"{ex.Message.Replace("\"", "\\\"")}\"}}]}}"
                    ))
                };
            }
            finally
            {
                _concurrencySemaphore.Release();
            }
        }

        private bool TryGetFromCache(string cacheKey, out RegistryResponse response)
        {
            response = null;

            try
            {
                if (!File.Exists(cacheKey))
                {
                    return false;
                }

                // Check if content type file exists
                string contentType = "application/octet-stream";
                if (File.Exists($"{cacheKey}.type"))
                {
                    contentType = File.ReadAllText($"{cacheKey}.type");
                }

                // Open a read stream to the file
                var fileStream = new FileStream(cacheKey, FileMode.Open, FileAccess.Read, FileShare.Read,
                                              _config.BufferSize, FileOptions.Asynchronous);

                response = new RegistryResponse
                {
                    StatusCode = 200,
                    ContentType = contentType,
                    Content = fileStream,
                    IsFileStream = true
                };

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error reading from cache: {CacheKey}", cacheKey);
                return false;
            }
        }

        private async Task SaveToCache(string cacheKey, string contentType, Stream content)
        {
            try
            {
                // Ensure directory exists
                EnsureDirectoryExists(Path.GetDirectoryName(cacheKey));

                // Write content to file
                using (var fileStream = new FileStream(cacheKey, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await content.CopyToAsync(fileStream);
                    await fileStream.FlushAsync();
                }

                // Write content type
                await File.WriteAllTextAsync($"{cacheKey}.type", contentType);

                Log.Debug("Saved to cache: {CacheKey}", cacheKey);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error saving to cache: {CacheKey}", cacheKey);
            }
            finally
            {
                content.Dispose();
            }
        }

        private string GetSafeFileName(string input)
        {
            // Hash the input for consistent file names
            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private void EnsureDirectoryExists(string dir)
        {
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
    }

    public class RegistryResponse
    {
        public int StatusCode { get; set; }
        public string ContentType { get; set; }
        public Stream Content { get; set; }
        public bool IsFileStream { get; set; } // Helps with proper disposal
    }
}