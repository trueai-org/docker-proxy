# Docker Proxy

自建Docker镜像加速服务。

```bash
# 1. 第一步启动容器
docker run --name dp -d --restart=always -p 8080:8080 registry.cn-guangzhou.aliyuncs.com/trueai-org/docker-proxy

# 2. 使用 Caddy 反向代理或使用 Nginx 配置 https

# 2.1 Caddy 示例
sudo nano /etc/caddy/Caddyfile
doman.com {
    reverse_proxy localhost:8080 {
        header_up Host {host}
        header_up X-Real-IP {remote}
        header_up X-Forwarded-For {remote}
        header_up X-Forwarded-Proto {scheme}
    }
    log {
        output file /var/log/caddy/doman.com.log
    }
}

# 3. 配置 Docker Daemon 或直接使用

# 3.1 直接使用
docker pull doman.com/library/redis

# 3.2 配置 Docker Daemon
sudo mkdir -p /etc/docker
sudo vi /etc/docker/daemon.json
{
  "registry-mirrors": ["https://<代理加速地址>"]
}

sudo systemctl daemon-reload
sudo systemctl restart docker
```

## 准备

⚠️ 重要：选择一台国外服务器，并且未被墙。对于域名，无需进行国内备案。你也可以通过一些平台申请免费域名。在一键部署过程中，如果选择安装Caddy，它将自动配置HTTPS。若选择部署Nginx服务，则需要自行申请一个免费的SSL证书，或者通过其他方式来实现SSL加密。

## 功能

一键部署Docker镜像代理服务的功能，支持基于官方Docker Registry的镜像代理。