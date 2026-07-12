@echo off
setlocal

cd /d "%~dp0"
set "PROJECT=rts-demo-1.csproj"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo.
    echo ERROR: dotnet was not found in PATH.
    echo Install the .NET 9 SDK or add dotnet to PATH.
    goto :failed
)

echo Restoring %PROJECT%...
dotnet restore "%PROJECT%" --verbosity minimal
if errorlevel 1 goto :failed

echo.
echo Building %PROJECT%...
dotnet build "%PROJECT%" --no-restore --verbosity minimal
if errorlevel 1 goto :failed

echo.
echo C# build completed successfully.
if /I not "%~1"=="--no-pause" pause
exit /b 0

:failed
echo.
echo C# build failed. Review the errors above.
if /I not "%~1"=="--no-pause" pause
exit /b 1

