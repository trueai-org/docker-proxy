using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace DockerProxy
{
    /// <summary>
    /// HttpContext 扩展
    /// </summary>
    public static class HttpContextExtensions
    {
        /// <summary>
        /// 获取请求主体内容
        /// </summary>
        /// <param name="httpRequest"></param>
        /// <returns></returns>
        public static string GetRequestBody(this HttpRequest httpRequest)
        {
            if (httpRequest == null)
            {
                return null;
            }

            httpRequest.EnableBuffering();

            //// 重置 position
            //httpRequest.Body.Seek(0, SeekOrigin.Begin);
            //// or
            ////httpRequest.Body.Position = 0;

            //StreamReader sr = new StreamReader(httpRequest.Body);

            //var content = sr.ReadToEndAsync().Result;

            //httpRequest.Body.Seek(0, SeekOrigin.Begin);
            //// or
            //// httpRequest.Body.Position = 0;

            StreamReader sr = new StreamReader(httpRequest.Body);
            var content = sr.ReadToEndAsync().Result;
            httpRequest.Body.Seek(0, SeekOrigin.Begin);
            return content;
        }

        /// <summary>
        /// 获取请求链接地址
        /// </summary>
        /// <param name="httpRequest"></param>
        /// <returns></returns>
        public static string GetUrl(this HttpRequest httpRequest)
        {
            if (httpRequest == null)
            {
                return string.Empty;
            }

            return new StringBuilder()
                .Append(httpRequest.Scheme).Append("://")
                .Append(httpRequest.Host).Append(httpRequest.PathBase)
                .Append(httpRequest.Path).Append(httpRequest.QueryString).ToString();
        }

        /// <summary>
        /// 获取客户端 IP 地址
        /// </summary>
        /// <param name="httpRequest"></param>
        /// <param name="ignoreLocalIpAddress">验证时是否忽略本地 IP 地址，如果忽略本地 IP 地址，则当判断为本地 IP 地址时返回可能为空</param>
        /// <returns></returns>
        public static string GetIP(this HttpRequest httpRequest, bool ignoreLocalIpAddress = false)
        {
            if (httpRequest == null)
            {
                return string.Empty;
            }

            var ip = string.Empty;

            // 可以被伪造（使用百度云加速时，百度云会自动移除此客户端请求头，且不可被伪造）
            // 获取True-Client-Ip头信息（百度云加速用户真实 IP）
            if (string.IsNullOrWhiteSpace(ip))
            {
                if (httpRequest.Headers.ContainsKey("True-Client-Ip"))
                {
                    if (httpRequest.Headers.TryGetValue("True-Client-Ip", out var tci))
                    {
                        ip = tci.ToString();
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(ip))
            {
                if (httpRequest.Headers.ContainsKey("X-Real-IP"))
                {
                    ip = httpRequest.Headers["X-Real-IP"].FirstOrDefault();
                }
            }

            if (string.IsNullOrWhiteSpace(ip))
            {
                if (httpRequest.Headers.ContainsKey("X-Forwarded-For"))
                {
                    ip = httpRequest.Headers["X-Forwarded-For"].FirstOrDefault();
                }
            }

            if (string.IsNullOrEmpty(ip))
            {
                var address = httpRequest.HttpContext.Connection.RemoteIpAddress;

                // compare with local address
                if (ignoreLocalIpAddress && address == httpRequest.HttpContext.Connection.LocalIpAddress)
                {
                    ip = string.Empty;
                }

                if (address?.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    ip = address?.MapToIPv4()?.ToString();
                }

                if (string.IsNullOrWhiteSpace(ip))
                {
                    ip = address?.ToString();
                }
            }

            if (string.IsNullOrWhiteSpace(ip))
            {
                ip = httpRequest.Host.Host ?? httpRequest.Host.Value;
            }

            if (!string.IsNullOrWhiteSpace(ip) && ip.Contains(","))
            {
                ip = ip.Split(',')[0];
            }

            return ip;
        }

        /// <summary>
        /// 获取客服端 User-Agent
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public static string GetUserAgent(this HttpRequest request)
        {
            if (request != null && request.Headers?.Count > 0 && request.Headers.ContainsKey("User-Agent"))
            {
                return request.Headers["User-Agent"].ToString();
            }
            return null;
        }

        /// <summary>
        /// 获取访客每日身份唯一标识（不依赖 cookies），用于用于访客统计。
        /// 根据请求生成每日变化的唯一标识符来统计唯一访客数。
        /// https://plausible.io/data-policy#how-we-count-unique-users-without-cookies
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public static string GetDailyVisitorId(this HttpRequest request)
        {
            if (request == null)
            {
                return null;
            }

            // 从请求中提取IP地址和 User-Agent
            var userAgent = request.GetUserAgent();
            var ip = request.GetIP();

            // 生成每日唯一盐值
            var dailySalt = GetDailySalt();

            // 构造待哈希的字符串
            var toHash = $"{dailySalt}+{request.Host}+{ip}+{userAgent}";

            // 生成哈希值
            var hashed = CreateHash(toHash);

            return hashed;
        }

        /// <summary>
        /// 生成基于当前日期的盐值。
        /// </summary>
        /// <returns>返回基于日期的盐值。</returns>
        private static string GetDailySalt()
        {
            return DateTime.Now.ToString("yyyyMMdd");
        }

        /// <summary>
        /// 使用SHA256算法对字符串进行哈希处理。
        /// </summary>
        /// <param name="input">待哈希的字符串。</param>
        /// <returns>返回哈希后的字符串。</returns>
        private static string CreateHash(string input)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                return BitConverter.ToString(hashedBytes).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
