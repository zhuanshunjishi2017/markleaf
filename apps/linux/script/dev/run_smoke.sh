#!/usr/bin/env bash
# 容器内无头冒烟:手动起 Xvfb(绕开 xvfb-run 包装脚本在容器 TTY 下的挂起问题),
# 设置 DISPLAY 与 WebKit 沙箱豁免(容器无 user namespace),运行冒烟后回收 Xvfb。
set -u

DISPLAY_ID=${MARKLEAF_SMOKE_DISPLAY:-:97}
Xvfb "$DISPLAY_ID" -screen 0 1024x768x24 -nolisten tcp &
xvfb_pid=$!

sleep 1
export DISPLAY="$DISPLAY_ID"
# 沙箱豁免:容器无 user namespace;合成模式关闭:xvfb 无 GPU,加速合成出空帧。
export WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS=1
export WEBKIT_DISABLE_COMPOSITING_MODE=1
# 2.42+ 的 DMABUF 渲染器在无 GPU 显示下出空白帧。
export WEBKIT_DISABLE_DMABUF_RENDERER=1
# GTK4 GL 渲染器在 Xvfb(无 GLX/EGL)下出空白帧;强制 Cairo 软件渲染。
export GSK_RENDERER=cairo

set +e
cargo run --bin smoke "$@"
rc=$?

kill "$xvfb_pid" 2>/dev/null || true
wait "$xvfb_pid" 2>/dev/null || true
exit "$rc"
