@echo off
chcp 65001 >nul
title NovaDown 完整卸载工具
echo ============================================
echo   NovaDown 完整卸载工具
echo   将清除：已安装程序、设置、全部下载文件
echo ============================================
echo.
echo 警告：本工具会删除以下内容，且不可恢复：
echo   1. 已安装的 NovaDown 程序（如已安装）
echo   2. 设置数据 %APPDATA%\NovaDown
echo   3. 下载目录 %USERPROFILE%\Downloads\NovaDown（含全部已下载文件）
echo.
choice /c YN /m "确认全部清除？(Y=确认 / N=取消)"
if errorlevel 2 exit /b 0

echo.
echo [1/4] 结束 NovaDown 进程...
taskkill /f /im NovaDown.App.exe >nul 2>&1
taskkill /f /im NovaDown.Cli.exe >nul 2>&1

echo [2/4] 运行安装版卸载程序（如已安装）...
set "UNINS="
for /f "skip=2 tokens=2,*" %%a in ('reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{7E1A4C2B-9D3F-4A58-8C6D-21B0E5F7A930}_is1" /v UninstallString 2^>nul') do set "UNINS=%%b"
if not defined UNINS for /f "skip=2 tokens=2,*" %%a in ('reg query "HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{7E1A4C2B-9D3F-4A58-8C6D-21B0E5F7A930}_is1" /v UninstallString 2^>nul') do set "UNINS=%%b"
if defined UNINS (
  echo   找到卸载程序：%UNINS%
  %UNINS% /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
  timeout /t 8 /nobreak >nul
) else (
  echo   未检测到安装版本，跳过。
)

echo [3/4] 清除残留程序目录...
if exist "%ProgramFiles%\NovaDown" rmdir /s /q "%ProgramFiles%\NovaDown"
if exist "%ProgramFiles(x86)%\NovaDown" rmdir /s /q "%ProgramFiles(x86)%\NovaDown"
if exist "%LocalAppData%\Programs\NovaDown" rmdir /s /q "%LocalAppData%\Programs\NovaDown"

echo [4/4] 清除设置与下载数据...
if exist "%APPDATA%\NovaDown" rmdir /s /q "%APPDATA%\NovaDown"
if exist "%USERPROFILE%\Downloads\NovaDown" rmdir /s /q "%USERPROFILE%\Downloads\NovaDown"

echo.
echo ============================================
echo   卸载完成。
echo   最后一步（需手动）：打开浏览器扩展管理页，
echo   删除 "NovaDown 接管助手" 扩展。
echo ============================================
pause
