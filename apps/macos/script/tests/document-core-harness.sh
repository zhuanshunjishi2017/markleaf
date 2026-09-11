#!/usr/bin/env bash
# Compile native adapters against the real shared artifact, without a second implementation.
compile_with_document_core() {
  local args=("$@") output="" index app executable kernel extra=()
  kernel="$ROOT_DIR/../../packages/editor-core/dist/document-kernel.cjs"
  if [[ ! -f "$kernel" ]]; then
    echo "Build the shared kernel with corepack pnpm build:kernel before running native adapter tests." >&2
    return 1
  fi
  for ((index=0; index<${#args[@]}; index++)); do
    if [[ "${args[index]}" == "-o" ]]; then output="${args[index+1]}"; break; fi
  done
  [[ -n "$output" ]] || { echo "Test compiler output is required" >&2; return 1; }
  app="$output.app"
  executable="$app/Contents/MacOS/AdapterTest"
  mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources/DocumentCore"
  args[index+1]="$executable"
  extra+=("$ROOT_DIR/Sources/MarkLeaf/Services/DocumentCoreRuntime.swift")
  if [[ " ${args[*]} " != *"/Support/AppLog.swift "* ]]; then extra+=("$ROOT_DIR/Sources/MarkLeaf/Support/AppLog.swift"); fi
  command swiftc "${args[@]}" "${extra[@]}"
  cp "$kernel" "$app/Contents/Resources/DocumentCore/document-kernel.cjs"
  cat > "$app/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict><key>CFBundleExecutable</key><string>AdapterTest</string><key>CFBundleIdentifier</key><string>com.markleaf.adapter-test</string><key>CFBundlePackageType</key><string>APPL</string></dict></plist>
PLIST
  printf '#!/usr/bin/env bash\nexec %q "$@"\n' "$executable" > "$output"
  chmod +x "$output"
}
