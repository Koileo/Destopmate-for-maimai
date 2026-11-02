@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

rem ============================================================
rem  DesktopMate for maimai - 一键构建
rem  用法: build.bat [麦麦Package目录]
rem  例:   build.bat D:\maimaiHDD\SDEZ170\Package
rem  依赖: .NET SDK 6/8（dotnet build）
rem ============================================================

set "MAIMAI=%~1"
if "%MAIMAI%"=="" set "MAIMAI=D:\BaiduNetdiskDownload\SDEZ170_nobga_v2\SDEZ170\Package"

rem 新安装默认使用 Miku；缺少资源时不要安装一份无法显示默认角色的配置。
for %%F in (character.dmc motion.dmm texture_0.png) do (
    if not exist "%~dp0assets\local\miku-local\%%F" (
        echo [错误] 缺少默认 Miku 资源: assets\local\miku-local\%%F
        echo        请先转换 Miku 资源，或直接安装附带 Miku 的发布包。
        exit /b 1
    )
)

if not exist "%MAIMAI%\MelonLoader\net35\MelonLoader.dll" (
    echo [错误] 找不到 %MAIMAI%\MelonLoader\net35\MelonLoader.dll
    echo        请把 Package 目录作为第一个参数传入。
    exit /b 1
)

rem 优先使用用户目录 SDK（不依赖重开终端后的 PATH）。
set "DOTNET_EXE=dotnet"
if exist "%LOCALAPPDATA%\Microsoft\dotnet-sdk-8\dotnet.exe" set "DOTNET_EXE=%LOCALAPPDATA%\Microsoft\dotnet-sdk-8\dotnet.exe"
if exist "%~dp0.tools\dotnet\dotnet.exe" set "DOTNET_EXE=%~dp0.tools\dotnet\dotnet.exe"
"%DOTNET_EXE%" --list-sdks 2>nul | findstr /r "^[0-9]" >nul
if errorlevel 1 (
    echo [错误] 未找到 .NET SDK，请先安装 .NET SDK 8。
    exit /b 1
)

echo [1/3] 构建 ...
"%DOTNET_EXE%" build "%~dp0DesktopMateOnMaimai.csproj" -c Release -p:MaimaiDir="%MAIMAI%" -v:m
if errorlevel 1 (
    echo [错误] 构建失败
    exit /b 1
)

set "OUT=%~dp0bin\Release\DesktopMateOnMaimai.dll"
if not exist "%OUT%" (
    echo [错误] 未生成 %OUT%
    exit /b 1
)

echo [2/3] 复制到 Mods ...
if not exist "%MAIMAI%\Mods" mkdir "%MAIMAI%\Mods"
copy /y "%OUT%" "%MAIMAI%\Mods\DesktopMateOnMaimai.dll" >nul
if errorlevel 1 exit /b 1

echo [3/3] 准备资源目录 ...
if not exist "%MAIMAI%\Mods\DesktopMate\Characters" mkdir "%MAIMAI%\Mods\DesktopMate\Characters"
if not exist "%MAIMAI%\Mods\DesktopMate\DesktopMate.toml" (
    copy /y "%~dp0assets\DesktopMate.toml" "%MAIMAI%\Mods\DesktopMate\DesktopMate.toml" >nul
    if errorlevel 1 exit /b 1
)
echo.
rem 本机转换的正式角色仅在本地存在；不与源码一起分发。
for /d %%C in ("%~dp0assets\local\*") do (
    if exist "%%~fC\character.dmc" (
        xcopy /e /i /y "%%~fC" "%MAIMAI%\Mods\DesktopMate\Characters\%%~nxC" >nul
        if errorlevel 1 exit /b 1
    )
)
echo 完成。启动游戏后，招揽待机时会自动出现角色，刷卡流程淡出隐藏。
echo 配置: %MAIMAI%\Mods\DesktopMate\DesktopMate.toml
echo 角色: %MAIMAI%\Mods\DesktopMate\Characters\^<id^>\
endlocal
