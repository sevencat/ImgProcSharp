@echo off
chcp 65001 >nul
setlocal

rem 打包 Sevencat.ImgproSharp 并推送到 nuget.org。
rem 用法：pack-and-push.bat                打包 + 上传
rem       pack-and-push.bat --pack-only    只打包
rem 密钥：环境变量 NUGET_API_KEY（不写入任何文件）

cd /d "%~dp0"

set "PROJECT=src\ImgProcSharp.SimdCodecs\ImgProcSharp.SimdCodecs.csproj"
set "PKGDIR=src\ImgProcSharp.SimdCodecs\bin\Release"
set "SOURCE=https://api.nuget.org/v3/index.json"

rem 别给 dotnet pack 传 -o / -p:PackageOutputPath 指定输出目录：
rem SDK 10 下它会被当成 build 输出目录，报 NU5026（去那个目录找 dll）。
rem 所以产包位置就是 bin\Release，pack 前先清掉旧包，避免拿到上个版本的 nupkg。
if exist "%PKGDIR%\*.nupkg" del /q "%PKGDIR%\*.nupkg"

echo [1/2] dotnet pack
dotnet pack "%PROJECT%" -c Release
if errorlevel 1 goto :fail

set "PKG="
for %%f in ("%PKGDIR%\*.nupkg") do set "PKG=%%~ff"
if not defined PKG (
    echo [ERROR] 没找到 nupkg，pack 可能失败了。
    goto :fail
)
echo     产物 %PKG%

if /i "%~1"=="--pack-only" (
    echo     已跳过上传。
    goto :done
)

if not defined NUGET_API_KEY (
    echo [ERROR] 环境变量 NUGET_API_KEY 未设置。
    echo         当前窗口临时设置： set NUGET_API_KEY=你的key
    echo         永久设置：         setx NUGET_API_KEY "你的key"   然后重开终端
    goto :fail
)

echo [2/2] dotnet nuget push
dotnet nuget push "%PKG%" --api-key "%NUGET_API_KEY%" --source "%SOURCE%" --skip-duplicate
if errorlevel 1 goto :fail

:done
echo 完成。
exit /b 0

:fail
echo 失败，退出码 %ERRORLEVEL%。
exit /b %ERRORLEVEL%
