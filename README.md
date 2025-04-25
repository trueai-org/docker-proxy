# Docker Proxy

自建Docker镜像加速服务。

镜像加速地址：<https://docker.qrco.cc>

镜像拉取测试：`docker pull docker.qrco.cc/library/redis`

## 功能

一键部署Docker镜像代理服务的功能，支持基于官方Docker Registry的镜像代理。

## 部署

> Docker 版本

```bash
# 一键升安装脚本
# 1.首次下载
wget -O docker-upgrade.sh https://raw.githubusercontent.com/trueai-org/docker-proxy/main/scripts/docker-upgrade.sh && bash docker-upgrade.sh

# 2.更新升级（以后升级只需要执行此脚本即可）
sh docker-upgrade.sh
```

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

> Windows 版本

```bash
a. 通过 https://github.com/trueai-org/docker-proxy/releases 下载 windows 最新免安装版，例如：midjourney-proxy-win-x64.zip
b. 解压并执行 DockerProxy.exe
c. 打开网站 http://localhost:8080
```

> Linux 版本

```bash
a. 通过 https://github.com/trueai-org/docker-proxy/releases 下载 linux 最新免安装版，例如：midjourney-proxy-linux-x64.zip
b. 解压到当前目录: tar -xzf docker-proxy-linux-x64-<VERSION>.tar.gz
c. 执行: run_app.sh
c. 启动方式1: sh run_app.sh
d. 启动方式2: chmod +x run_app.sh && ./run_app.sh
```

> macOS 版本

```bash
a. 通过 https://github.com/trueai-org/docker-proxy/releases 下载 macOS 最新免安装版，例如：midjourney-proxy-osx-x64.zip
b. 解压到当前目录: tar -xzf docker-proxy-osx-x64-<VERSION>.tar.gz
c. 执行: run_app_osx.sh
c. 启动方式1: sh run_app_osx.sh
d. 启动方式2: chmod +x run_app_osx.sh && ./run_app_osx.sh
```

> Registry 配置项 appsettings.json

- `CacheDir`：缓存目录，默认值为 `./cache`。
- `CacheTTL`：文件缓存过期时间，单位为秒，默认值为 `604800`（7天）。
- `Timeout`：请求超时时间，单位为毫秒，默认值为 `30000`（30秒）。
- `MemoryLimit`：内存限制，单位为MB，默认值为 `128`（128MB）。
- `BufferSize`：缓冲区大小，单位为字节，默认值为 `8192`（8KB）。
- `Username`：Docker Hub 用户名，默认值为空，采用登录方式可提升拉取速度。
- `Password`：密码，默认值为空，采用登录方式可提升拉取速度。

```json
{
    "CacheDir": "./cache",
    "CacheTTL": 604800,
    "Timeout": 30000,
    "MemoryLimit": 128,
    "BufferSize": 8192,
    "Username": "",
    "Password": ""
}
```

## 准备

⚠️ 重要：选择一台国外服务器，并且未被墙。对于域名，无需进行国内备案。你也可以通过一些平台申请免费域名。在一键部署过程中，如果选择安装Caddy，它将自动配置HTTPS。若选择部署Nginx服务，则需要自行申请一个免费的SSL证书，或者通过其他方式来实现SSL加密。

