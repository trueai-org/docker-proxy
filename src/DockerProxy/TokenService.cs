using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Serilog;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;

namespace DockerProxy
{
    public class TokenService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private readonly AppConfig _config;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        private class TokenResponse
        {
            [JsonPropertyName("token")]
            public string Token { get; set; }

            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; }

            [JsonPropertyName("expires_in")]
            public int? ExpiresIn { get; set; }

            [JsonPropertyName("issued_at")]
            public DateTimeOffset IssuedAt { get; set; }
        }

        public TokenService(IHttpClientFactory httpClientFactory, IMemoryCache cache, IOptions<AppConfig> config)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
            _config = config.Value;
        }

        public async Task<string> GetTokenAsync(string scope)
        {
            string cacheKey = $"token:{scope}";

            // Try to get from memory cache
            if (_cache.TryGetValue<string>(cacheKey, out var cachedToken))
            {
                Log.Debug("Using cached token for {Scope}", scope);
                return cachedToken;
            }

            // Prevent multiple simultaneous requests for the same token
            await _semaphore.WaitAsync();

            try
            {
                // Double check after acquiring semaphore
                if (_cache.TryGetValue<string>(cacheKey, out cachedToken))
                {
                    return cachedToken;
                }

                Log.Information("Getting token for {Scope}", scope);

                // Fetch from Docker Hub auth service
                // DockerRegistry
                var client = _httpClientFactory.CreateClient("registry");

                // Add basic auth if username and password are provided
                if (!string.IsNullOrEmpty(_config.Username) && !string.IsNullOrEmpty(_config.Password))
                {
                    var credentials = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{_config.Username}:{_config.Password}"));
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
                }

                var uriBuilder = new UriBuilder("https://auth.docker.io/token");
                var query = HttpUtility.ParseQueryString(string.Empty);
                query["scope"] = scope;
                query["service"] = "registry.docker.io";

                // Add credentials if available
                if (!string.IsNullOrEmpty(_config.Username) && !string.IsNullOrEmpty(_config.Password))
                {
                    query["account"] = _config.Username;
                }

                uriBuilder.Query = query.ToString();

                HttpResponseMessage response = await client.GetAsync(uriBuilder.ToString());

                if (!response.IsSuccessStatusCode)
                {
                    Log.Error("Failed to get token: {StatusCode}", response.StatusCode);
                    return null;
                }

                string json = await response.Content.ReadAsStringAsync();
                var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(json);

                string token = tokenResponse.Token ?? tokenResponse.AccessToken;
                if (string.IsNullOrEmpty(token))
                {
                    Log.Error("Invalid token response: {Json}", json);
                    return null;
                }

                _cache.Set(cacheKey, token, new MemoryCacheEntryOptions
                {
                    AbsoluteExpiration = tokenResponse.IssuedAt.AddSeconds(tokenResponse.ExpiresIn ?? 300),
                    Size = token.Length * 2
                });

                Log.Information("Token obtained successfully for {Scope}", scope);
                return token;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error getting token for {Scope}", scope);
                return null;
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}