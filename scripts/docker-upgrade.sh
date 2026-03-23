#!/bin/bash

# 定义一些变量
IMAGE_NAME="registry.cn-guangzhou.aliyuncs.com/trueai-org/docker-proxy"
CONTAINER_NAME="docker-proxy"

echo "开始更新 ${CONTAINER_NAME} 容器..."

# 创建网络（已存在则忽略）
docker network create ${NETWORK_NAME} 2>/dev/null || true

# 拉取最新镜像
echo "拉取最新的镜像 ${IMAGE_NAME}..."
docker pull ${IMAGE_NAME} || { echo "拉取镜像失败"; exit 1; }

# 强制停止并移除容器（如果存在）
docker rm -f ${CONTAINER_NAME} 2>/dev/null || true

# 运行新的容器
echo "启动新的容器 ${CONTAINER_NAME}..."

mkdir -p ./logs
mkdir -p ./cache
mkdir -p ./cache/manifests
mkdir -p ./cache/blobs

# 添加权限修改
chmod -R 777 ./cache
chmod -R 777 ./logs

docker run --name ${CONTAINER_NAME} -d --restart=always \
 -p 8090:8080 \
 --cpus="1" \
 -v ./logs:/app/logs:rw \
 -v ./cache:/app/cache:rw \
 -v ./appsettings.json:/app/appsettings.json:ro \
 -e TZ=Asia/Shanghai \
 --log-opt max-size=10m \
 --log-opt max-file=10 \
 ${IMAGE_NAME} || { echo "启动容器失败"; exit 1; }

echo "容器 ${CONTAINER_NAME} 更新并启动成功！"