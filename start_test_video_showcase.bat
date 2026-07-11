@echo off
setlocal

cd /d "%~dp0"
set "SHOWCASE_URL=http://localhost:10086/test_videos/showcase/"

echo Updating the test video index...
powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\generate_test_video_showcase.ps1"
if errorlevel 1 (
    echo.
    echo Failed to update the test video index.
    pause
    exit /b 1
)

where python >nul 2>nul
if errorlevel 1 (
    echo.
    echo Python was not found in PATH. Install Python or add it to PATH first.
    pause
    exit /b 1
)

echo.
echo Opening %SHOWCASE_URL%
echo Press Ctrl+C or close this window to stop the server.
echo.

start "" powershell -NoProfile -WindowStyle Hidden -Command "Start-Sleep -Milliseconds 900; Start-Process '%SHOWCASE_URL%'"
python -m http.server 10086 --bind 127.0.0.1

endlocal
