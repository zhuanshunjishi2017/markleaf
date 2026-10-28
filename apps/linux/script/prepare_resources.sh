#!/usr/bin/env bash
# 准备 Linux 应用资源(镜像 apps/macos/script/prepare_resources.sh):
#   1) 构建 EditorWeb 前端(dist);MARKLEAF_USE_BUILT_EDITOR_WEB=1 时复用已有构建
#   2) 复制 dist 到 markleaf/resources/editor,注入 Linux 版 native-shim.js
#      (无需改 CSP:内核 index.html 已含 markleaf-asset:)
#   3) 生成 asset-manifest.json(相对路径 → SHA-256),供冒烟与发布校验
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPO_DIR="$(cd "$ROOT_DIR/../.." && pwd)"
EDITOR_WEB_DIR="$REPO_DIR/packages/editor-web"
EDITOR_CORE_DIR="$REPO_DIR/packages/editor-core"
RESOURCES_EDITOR="$ROOT_DIR/markleaf/resources/editor"

if [ "${MARKLEAF_USE_BUILT_EDITOR_WEB:-0}" != "1" ]; then
  if [ ! -d "$EDITOR_CORE_DIR/node_modules" ]; then
    echo "[prepare] 安装共享渲染内核依赖 (pnpm install)..."
    pnpm --dir "$EDITOR_CORE_DIR" install --frozen-lockfile
  fi
  if [ ! -d "$EDITOR_WEB_DIR/node_modules" ]; then
    echo "[prepare] 安装前端依赖 (pnpm install)..."
    pnpm --dir "$EDITOR_WEB_DIR" install --frozen-lockfile
  fi
  echo "[prepare] 构建共享渲染内核 (renderer)..."
  pnpm --dir "$EDITOR_CORE_DIR" build:renderer
  echo "[prepare] 构建 EditorWeb (pnpm build)..."
  pnpm --dir "$EDITOR_WEB_DIR" build
fi

if [ ! -f "$EDITOR_WEB_DIR/dist/index.html" ]; then
  echo "[prepare] 未找到 $EDITOR_WEB_DIR/dist/index.html,请先在根仓执行 corepack pnpm build:editor-web" >&2
  exit 1
fi

rm -rf "$RESOURCES_EDITOR"
mkdir -p "$RESOURCES_EDITOR"
cp -R "$EDITOR_WEB_DIR/dist/." "$RESOURCES_EDITOR/"

# 注入 Linux 桥(shim 模板由仓库检出,便于评审与测试)
cp "$ROOT_DIR/markleaf/resources/native-shim.js" "$RESOURCES_EDITOR/native-shim.js"
if ! grep -q 'native-shim.js' "$RESOURCES_EDITOR/index.html"; then
  python3 - "$RESOURCES_EDITOR/index.html" <<'PY_EOF'
import sys
path = sys.argv[1]
with open(path, "r", encoding="utf-8") as f:
    html = f.read()
html = html.replace("</head>", '<script src="native-shim.js"></script></head>', 1)
with open(path, "w", encoding="utf-8") as f:
    f.write(html)
print("[prepare] index.html 已注入 native-shim.js")
PY_EOF
fi

# 资产清单(SHA-256)
python3 - "$RESOURCES_EDITOR" <<'PY_EOF'
import hashlib, json, os, sys
root = sys.argv[1]
manifest = {}
for dirpath, _dirnames, filenames in os.walk(root):
    for name in filenames:
        full = os.path.join(dirpath, name)
        rel = os.path.relpath(full, root).replace(os.sep, "/")
        with open(full, "rb") as f:
            manifest[rel] = hashlib.sha256(f.read()).hexdigest()
with open(os.path.join(root, "asset-manifest.json"), "w", encoding="utf-8") as f:
    json.dump(manifest, f, sort_keys=True, indent=1)
print(f"[prepare] asset-manifest.json:{len(manifest)} 个文件")
PY_EOF
