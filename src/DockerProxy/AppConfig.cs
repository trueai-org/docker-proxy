namespace DockerProxy
{
    public class AppConfig
    {
        public int Port { get; set; } = 8080;
        public string CacheDir { get; set; } = "./cache";
        public int CacheTTL { get; set; } = 604800; // 7 days in seconds
        public int Timeout { get; set; } = 30000; // 30 seconds
        public int MemoryLimit { get; set; } = 128; // 128 MB
        public int BufferSize { get; set; } = 8192; // 8 KB


        // Optional credentials for Docker Hub (for higher rate limits)
        public string Username { get; set; }

        public string Password { get; set; }
    }
}