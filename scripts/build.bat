@echo off
:: Apricadabra Build Script
:: Usage:
::   scripts\build.bat              Build everything
::   scripts\build.bat core         Build core only
::   scripts\build.bat trackpad     Build trackpad plugin
::   scripts\build.bat sdk          Build C# client SDK
::   scripts\build.bat loupedeck    Build Loupedeck plugin
::   scripts\build.bat streamdeck   Build Stream Deck plugin
::   scripts\build.bat core trackpad  Multiple targets

setlocal enabledelayedexpansion
set "ROOT=%~dp0.."
set "BUILT="
set "FAILED="
set "HAD_FAILURE=0"

if "%~1"=="" (
    call :build core
    call :build sdk
    call :build loupedeck
    call :build streamdeck
    call :build trackpad
) else (
    :argloop
    if "%~1"=="" goto summary
    call :build %1
    shift
    goto argloop
)

:summary
echo.
echo --- Build Summary ---
if defined BUILT echo   OK: %BUILT%
if defined FAILED (
    echo   FAILED: %FAILED%
    exit /b 1
)
exit /b 0

:build
set "TARGET=%~1"

if /i "%TARGET%"=="core" (
    echo.
    echo [Building Core ^(Rust^)]
    pushd "%ROOT%\core"
    cargo build --release
    if errorlevel 1 (
        echo   FAIL: core
        set "FAILED=!FAILED! core"
        set "HAD_FAILURE=1"
    ) else (
        echo   OK: apricadabra-core.exe
        set "BUILT=!BUILT! core"
    )
    popd
    goto :eof
)

if /i "%TARGET%"=="sdk" (
    echo.
    echo [Building Apricadabra.Client SDK]
    pushd "%ROOT%\core\sdk\csharp\Apricadabra.Client"
    dotnet build -c Release
    if errorlevel 1 (
        echo   FAIL: sdk
        set "FAILED=!FAILED! sdk"
    ) else (
        echo   OK: Apricadabra.Client.dll
        set "BUILT=!BUILT! sdk"
    )
    popd
    goto :eof
)

if /i "%TARGET%"=="loupedeck" (
    echo.
    echo [Building Loupedeck Plugin]
    pushd "%ROOT%\loupedeck-plugin\ApricadabraPlugin\src"
    dotnet build -c Release
    if errorlevel 1 (
        echo   FAIL: loupedeck
        set "FAILED=!FAILED! loupedeck"
    ) else (
        echo   OK: ApricadabraPlugin.dll
        set "BUILT=!BUILT! loupedeck"
    )
    popd
    goto :eof
)

if /i "%TARGET%"=="streamdeck" (
    echo.
    echo [Building Stream Deck Plugin]
    pushd "%ROOT%\streamdeck-plugin"
    call npm run build
    if errorlevel 1 (
        echo   FAIL: streamdeck
        set "FAILED=!FAILED! streamdeck"
    ) else (
        echo   OK: plugin.js
        set "BUILT=!BUILT! streamdeck"
    )
    popd
    goto :eof
)

if /i "%TARGET%"=="trackpad" (
    echo.
    echo [Building Trackpad Plugin]
    pushd "%ROOT%\trackpad-plugin\Apricadabra.Trackpad"
    dotnet build -c Release
    if errorlevel 1 (
        echo   FAIL: trackpad
        set "FAILED=!FAILED! trackpad"
    ) else (
        echo   OK: Apricadabra.Trackpad.exe
        set "BUILT=!BUILT! trackpad"
    )
    popd
    goto :eof
)

echo   Unknown target: %TARGET%
echo   Valid targets: core sdk loupedeck streamdeck trackpad
set "FAILED=!FAILED! %TARGET%"
goto :eof
