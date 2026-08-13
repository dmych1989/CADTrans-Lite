@echo off
setlocal

REM ── 环境变量（保持与原开发机一致，便于 NuGet 缓存等路径解析）──
set "USERPROFILE=C:\Users\Administrator"
set "APPDATA=C:\Users\Administrator\AppData\Roaming"
set "LOCALAPPDATA=C:\Users\Administrator\AppData\Local"
set "HOME=C:\Users\Administrator"
set "NUGET_PACKAGES=C:\Users\Administrator\.nuget\packages"

REM ── 工作区路径（修正：原写死的 E:\CADTrans Lite 在本机不存在）──
set "SRC_DIR=d:\GitHub\CADTrans Lite\src"
set "SLN=CADTransLite.sln"

REM ── 选择 dotnet：优先使用本用户目录安装、已含 SDK 的本地 dotnet，
REM    否则回退到 PATH 中的 dotnet（注意：C:\Program Files\dotnet 仅有运行时，无法构建）──
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
