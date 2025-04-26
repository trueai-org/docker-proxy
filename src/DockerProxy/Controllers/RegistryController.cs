using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Serilog;

namespace DockerProxy.Controllers
{
    [ApiController]
    [Route("/")]
    public class RegistryController : ControllerBase
    {
        private readonly DockerRegistryService _registryService;
        private readonly AppConfig _config;
        private static readonly DateTime _startTime = DateTime.UtcNow;

        public RegistryController(DockerRegistryService registryService, IOptions<AppConfig> config)
        {
            _registryService = registryService;
            _config = config.Value;
        }

        [HttpGet()]
        [HttpGet("status")]
        [HttpGet("v1/status")]
        public IActionResult GetStatus()
        {
            var ip = Request.GetIP();

            var status = new
            {
                status = "ok",
                version = "1.2.3",
                timestamp = DateTime.UtcNow,
                uptime = (int)(DateTime.UtcNow - _startTime).TotalSeconds + " s",
                memoryLimit = $"{_config.MemoryLimit} MB",
                author = "trueai-org",
                ip
            };

            return Ok(status);
        }
        /// <summary>
        /// official Docker Registry API v2 only specifies these core endpoints
        /// /v2/ - API version check
        /// /v2/<name>/manifests/<reference> - For manifest operations
        /// /v2/<name>/blobs/<digest> - For blob operations
        /// /v2/<name>/tags/list - List tags
        /// /v2/_catalog - List repositories
        /// </summary>
        /// <returns></returns>
        [HttpGet("v2")]
        public IActionResult HandleV2Root()
        {
            Response.Headers.Append("Docker-Distribution-API-Version", "registry/2.0");
            return Ok();
        }

        [HttpGet("v2/{**path}")]
        public async Task<IActionResult> HandleV2Request(string path)
        {
            Response.Headers.Append("Docker-Distribution-API-Version", "registry/2.0");

            var url = Request.GetUrl();
            var ip = Request.GetIP();

            Log.Information("Received request: {Method} {Url} from {IP}", Request.Method, url, ip);

            // Handle manifest requests
            var manifestMatch = Regex.Match(path, @"^([^/]+(?:/[^/]+)?)/manifests/(.+)$");
            if (manifestMatch.Success)
            {
                string repository = manifestMatch.Groups[1].Value;
                if (!repository.Contains("/"))
                {
                    repository = $"library/{repository}";
                }

                string reference = manifestMatch.Groups[2].Value;
                Log.Information("Handling manifest request: {Repository}:{Reference}", repository, reference);

                var response = await _registryService.GetManifestAsync(repository, reference);
                return SendRegistryResponse(response);
            }

            // Handle blob requests
            var blobMatch = Regex.Match(path, @"^([^/]+(?:/[^/]+)?)/blobs/(.+)$");
            if (blobMatch.Success)
            {
                string repository = blobMatch.Groups[1].Value;
                if (!repository.Contains("/"))
                {
                    repository = $"library/{repository}";
                }

                string digest = blobMatch.Groups[2].Value;
                Log.Information("Handling blob request: {Repository}:{Digest}", repository, digest);

                var response = await _registryService.GetBlobAsync(repository, digest);
                return SendRegistryResponse(response);
            }

            // Unsupported endpoint
            return NotFound(new
            {
                errors = new[]
                {
                    new { code = "NOT_FOUND", message = $"Endpoint not found: /v2/{path}" }
                }
            });
        }

        private IActionResult SendRegistryResponse(RegistryResponse response)
        {
            try
            {
                if (response.StatusCode != 200)
                {
                    // For JSON error responses, we need to read the content stream and return it correctly
                    if (response.ContentType.Contains("application/json"))
                    {
                        // Read the stream into a string
                        string errorContent;
                        using (var reader = new StreamReader(response.Content))
                        {
                            errorContent = reader.ReadToEnd();
                        }

                        // IMPORTANT: Set content type header properly
                        Response.ContentType = "application/json";

                        // Return a ContentResult to ensure we have control over the exact response format
                        return StatusCode(response.StatusCode, new ContentResult
                        {
                            Content = errorContent,
                            ContentType = "application/json",
                            StatusCode = response.StatusCode
                        });
                    }
                    else
                    {
                        // For non-JSON errors, use the standard approach
                        byte[] buffer = new byte[response.Content.Length];
                        response.Content.Read(buffer, 0, buffer.Length);
                        response.Content.Dispose();

                        return StatusCode(response.StatusCode, buffer);
                    }

                    //// 配置输出类型
                    //if (!string.IsNullOrWhiteSpace(response.ContentType))
                    //{
                    //    Response.ContentType = response.ContentType;
                    //}
                    //else
                    //{
                    //    Response.ContentType = "application/json";
                    //}

                    //return StatusCode(response.StatusCode, response.Content);

                    //// For JSON error responses, we need to read the content stream and return it correctly
                    //if (response.ContentType.Contains("application/json"))
                    //{
                    //    // Read the stream into a string
                    //    string errorContent;
                    //    using (var reader = new StreamReader(response.Content))
                    //    {
                    //        errorContent = reader.ReadToEnd();
                    //    }

                    //    // Return a proper error response with the correct status code
                    //    return StatusCode(response.StatusCode, errorContent);
                    //}
                    //else
                    //{
                    //    // For non-JSON errors, use the standard approach
                    //    byte[] buffer = new byte[response.Content.Length];
                    //    response.Content.Read(buffer, 0, buffer.Length);
                    //    response.Content.Dispose();

                    //    return StatusCode(response.StatusCode, buffer);
                    //}
                }

                // IMPORTANT: Use different approach based on stream type
                if (response.IsFileStream)
                {
                    // For file streams, use the built-in FileStreamResult
                    // This safely handles the stream lifecycle
                    return new FileStreamResult(response.Content, response.ContentType);
                }
                else
                {
                    // For memory streams, copy to byte array to avoid disposal issues
                    byte[] buffer = new byte[response.Content.Length];
                    response.Content.Read(buffer, 0, buffer.Length);
                    response.Content.Dispose(); // Important: dispose the original stream

                    return File(buffer, response.ContentType);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error sending registry response");
                response.Content?.Dispose();

                return StatusCode(500, new
                {
                    errors = new[]
                    {
                        new { code = "SERVER_ERROR", message = ex.Message }
                    }
                });
            }
        }
    }
}