@echo off
setlocal

rem Build from the repository root so the script also works when started by right-click.
pushd "%~dp0"

set "CODEX_RUNTIME_ROOT=%USERPROFILE%\.cache\codex-runtimes\codex-primary-runtime\dependencies"
set "CODEX_NODE_BIN=%CODEX_RUNTIME_ROOT%\node\bin"
set "CODEX_PNPM=%CODEX_RUNTIME_ROOT%\bin\fallback\pnpm.cmd"
if not exist "%CODEX_PNPM%" (
    echo ERROR: Codex bundled pnpm was not found:
    echo        %CODEX_PNPM%
    set "BUILD_EXIT=1"
    goto build_end
)
set "PATH=%CODEX_NODE_BIN%;%CODEX_RUNTIME_ROOT%\bin\fallback;%PATH%"

echo [1/3] Checking required tools...
where dotnet >nul 2>&1
if errorlevel 1 (
    echo ERROR: dotnet was not found in PATH.
    set "BUILD_EXIT=1"
    goto build_end
)

echo [2/3] Building EditorWeb resources...
call "%CODEX_PNPM%" --dir "%~dp0src\EditorWeb" build
if errorlevel 1 (
    echo ERROR: EditorWeb build failed.
    set "BUILD_EXIT=1"
    goto build_end
)

echo [3/3] Restoring and building the .NET solution...
dotnet restore "%~dp0MarkLeaf.slnx"
if errorlevel 1 (
    echo ERROR: .NET restore failed.
    set "BUILD_EXIT=1"
    goto build_end
)

dotnet build "%~dp0MarkLeaf.slnx" --no-restore
if errorlevel 1 (
    echo ERROR: .NET build failed.
    set "BUILD_EXIT=1"
    goto build_end
)

set "BUILD_EXIT=0"
echo.
echo Build completed successfully.

:build_end
popd
if "%BUILD_EXIT%"=="0" (
    pause
) else (
    echo.
    echo Build failed. Press any key to close.
    pause >nul
)
exit /b %BUILD_EXIT%
