@echo off
setlocal

REM ── CADTrans-Lite 构建脚本（可移植：路径从本文件位置推导，不写死机器路径）──
set "SRC_DIR=%~dp0src"
set "SLN=CADTransLite.sln"

REM ── 选择 dotnet：优先使用本用户目录安装、已含 SDK 的本地 .NET 9，
REM    否则回退到 PATH 中的 dotnet（需自行安装 .NET 9 SDK）。──
set "LOCAL_DOTNET=%LOCALAPPDATA%\dotnet9\dotnet.exe"
if exist "%LOCAL_DOTNET%" (
    set "DOTNET=%LOCAL_DOTNET%"
) else (
    set "DOTNET=dotnet"
)

cd /d "%SRC_DIR%" || (echo [错误] 找不到目录: %SRC_DIR% && exit /b 1)

echo [build] 使用 dotnet: %DOTNET%
echo [build] 解决方案: %SRC_DIR%\%SLN%
"%DOTNET%" build %SLN% -c Debug
if errorlevel 1 (echo [错误] 构建失败 & exit /b 1)

echo [完成] 构建成功
endlocal
