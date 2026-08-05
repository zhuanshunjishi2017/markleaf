@echo off
setlocal

rem Build from the repository root so the script also works when started by right-click.
pushd "%~dp0"

echo Restoring and building the .NET solution...
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
