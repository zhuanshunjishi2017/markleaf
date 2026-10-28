#!/usr/bin/env bash
# 在 ubuntu-24.04 容器内构建/测试/冒烟 Linux 宿主(开发机为 macOS 时使用)。
# 用法:
#   container.sh build [cargo 参数...]   # cargo build
#   container.sh test [cargo 参数...]    # cargo test
#   container.sh clippy [cargo 参数...]  # cargo clippy
#   container.sh smoke [cargo 参数...]   # xvfb-run cargo run --bin smoke
#   container.sh run <命令...>           # 容器内执行任意命令
# 环境:MARKLEAF_DEV_IMAGE(默认 markleaf-linux-dev:24.04)、
#      MARKLEAF_DEV_ENGINE(docker|podman,自动探测)。
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/../../../.." && pwd)"
IMAGE="${MARKLEAF_DEV_IMAGE:-markleaf-linux-dev:24.04}"
ENGINE="${MARKLEAF_DEV_ENGINE:-}"

if [ -z "$ENGINE" ]; then
  for candidate in docker podman; do
    if command -v "$candidate" >/dev/null 2>&1 && "$candidate" info >/dev/null 2>&1; then
      ENGINE="$candidate"
      break
    fi
  done
fi
if [ -z "$ENGINE" ]; then
  echo "[container] 未找到可用的容器运行时。请安装并启动:brew install colima && colima start" >&2
  exit 1
fi

if ! "$ENGINE" image inspect "$IMAGE" >/dev/null 2>&1; then
  echo "[container] 构建开发镜像 $IMAGE(首次较慢)..."
  "$ENGINE" build -t "$IMAGE" -f "$SCRIPT_DIR/Dockerfile.ubuntu24" "$SCRIPT_DIR"
fi

# cargo 注册表缓存放仓库外,避免重复下载依赖。
CARGO_CACHE="${MARKLEAF_DEV_CARGO_CACHE:-$HOME/.cache/markleaf-linux-dev/cargo}"
mkdir -p "$CARGO_CACHE"

cmd="${1:-}"
if [ $# -gt 0 ]; then shift; fi
case "$cmd" in
  build)  inside=(cargo build "$@") ;;
  test)   inside=(cargo test "$@") ;;
  clippy) inside=(cargo clippy "$@") ;;
  # 冒烟经 run_smoke.sh:手动 Xvfb + 沙箱豁免(xvfb-run 包装脚本在容器
  # TTY 下会卡在 Xvfb 启动后;容器无 user namespace,WebKit 沙箱需豁免)。
  smoke)  inside=(bash /repo/apps/linux/script/dev/run_smoke.sh "$@") ;;
  run)    inside=("$@") ;;
  *) echo "用法: container.sh build|test|smoke|clippy|run [args...]" >&2; exit 2 ;;
esac

exec "$ENGINE" run --rm -t \
  -v "$REPO_DIR":/repo \
  -v "$CARGO_CACHE":/usr/local/cargo/registry \
  --workdir /repo/apps/linux \
  "$IMAGE" "${inside[@]}"
