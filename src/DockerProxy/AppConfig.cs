namespace DockerProxy
{
    /// <summary>
    /// 
    /// </summary>
    public class AppConfig
    {
        /// <summary>
        /// 演示环境
        /// </summary>
        public bool IsDemo { get; set; } = false;

        /// <summary>
        /// 缓存目录
        /// </summary>
        public string CacheDir { get; set; } = "./cache";

        /// <summary>
        /// 文件缓存过期时间（秒）
        /// </summary>
        public int CacheTTL { get; set; } = 604800; // 7 days in seconds

        /// <summary>
        /// 超时
        /// </summary>
        public int Timeout { get; set; } = 30000; // 30 seconds

        /// <summary>
        /// 内存限制（MB）
        /// </summary>
        public int MemoryLimit { get; set; } = 128; // 128 MB

        /// <summary>
        /// 缓冲区大小（字节）
        /// </summary>
        public int BufferSize { get; set; } = 8192; // 8 KB

        /// <summary>
        /// 并发下载数量
        /// </summary>
        public int Concurrent { get; set; } = 4;

        /// <summary>
        /// Optional credentials for Docker Hub (for higher rate limits)
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// Optional credentials for Docker Hub (for higher rate limits)
        /// </summary>
        public string Password { get; set; }
    }
}