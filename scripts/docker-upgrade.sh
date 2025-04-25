#!/bin/bash

# 定义一些变量
IMAGE_NAME="registry.cn-guangzhou.aliyuncs.com/trueai-org/docker-proxy"
CONTAINER_NAME="docker-proxy"

# 打印信息
echo "开始更新 ${CONTAINER_NAME} 容器..."

# 验证Docker是否安装
if ! command -v docker &> /dev/null && ! command -v docker.io &> /dev/null
then
    # 再尝试查找 docker 可执行文件
    if [ ! -x /usr/bin/docker ] && [ ! -x /usr/bin/docker.io ]
    then
        echo "Docker 未安装，请先安装 Docker。"
        exit 1
    fi
fi

# 如果脚本运行到这里，说明找到了 Docker
echo "Docker 已安装，继续执行..."

# 拉取最新镜像
echo "拉取最新的镜像 ${IMAGE_NAME}..."
docker pull ${IMAGE_NAME}
if [ $? -ne 0 ]; then
    echo "拉取镜像失败，请检查网络连接或镜像地址是否正确。"
    exit 1
fi

# 停止并移除现有容器
if [ "$(docker ps -q -f name=${CONTAINER_NAME})" ]; then
    echo "停止现有的容器 ${CONTAINER_NAME}..."
    docker stop ${CONTAINER_NAME}
    if [ $? -ne 0 ]; then
        echo "停止容器失败，请手动检查。"
        exit 1
    fi
fi

if [ "$(docker ps -aq -f status=exited -f name=${CONTAINER_NAME})" ]; then
    echo "移除现有的容器 ${CONTAINER_NAME}..."
    docker rm ${CONTAINER_NAME}
    if [ $? -ne 0 ]; then
        echo "移除容器失败，请手动检查。"
        exit 1
    fi
fi

# 运行新的容器
echo "启动新的容器 ${CONTAINER_NAME}..."
docker run --name ${CONTAINER_NAME} -d --restart=always \
 -p 8080:8080 \
 -e TZ=Asia/Shanghai \
 ${IMAGE_NAME}
if [ $? -ne 0 ]; then
    echo "启动新的容器失败，请手动检查。"
    exit 1
fi

echo "容器 ${CONTAINER_NAME} 更新并启动成功！"

