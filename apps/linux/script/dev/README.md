# Linux 宿主开发环境(容器)

开发机为 macOS 时,Linux 构建与冒烟在本容器内进行(与 CI `linux.yml` 的 ubuntu-24.04 环境同构)。

## 依赖

- 容器运行时:Docker Desktop / OrbStack / colima 任一。colima 用户:`brew install colima && colima start`
- 根仓 Node 22.12+ 与 Corepack pnpm 11.9.0(内核产物构建仍在宿主机执行)

## 用法

```bash
# 首次会自动构建镜像 markleaf-linux-dev:24.04(GTK4/adw/WebKitGTK6 + rust stable)
apps/linux/script/dev/container.sh test          # cargo test
apps/linux/script/dev/container.sh build         # cargo build
apps/linux/script/dev/container.sh clippy        # cargo clippy
apps/linux/script/dev/container.sh smoke         # xvfb-run cargo run --bin smoke
apps/linux/script/dev/container.sh run cargo --version
```

- 仓库根挂载为 `/repo`,工作目录 `/repo/apps/linux`
- cargo 注册表缓存在 `~/.cache/markleaf-linux-dev/cargo`,可用 `MARKLEAF_DEV_CARGO_CACHE` 覆盖
- 内核产物:先在宿主机执行 `apps/linux/script/prepare_resources.sh`(或设 `MARKLEAF_USE_BUILT_EDITOR_WEB=1` 复用已有 dist),容器内即可冒烟
- 注意:容器内以 root 构建,`apps/linux/target` 归属 root(已 gitignore,无碍)
