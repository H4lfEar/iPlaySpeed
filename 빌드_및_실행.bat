@echo off
cd /d "%~dp0"
title iPlaySpeed Build and Run

echo ============================================
echo    iPlaySpeed - Build and Run
echo ============================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
  echo [!] .NET SDK not found.
  echo     Install ".NET 8 SDK" from:
  echo        https://dotnet.microsoft.com/download/dotnet/8.0
  echo     Then run this file again.
  echo.
  pause
  exit /b 1
)

echo [1/2] Running core-logic verification tests...
dotnet test "src\IPlaySpeed.Core.Tests\IPlaySpeed.Core.Tests.csproj" -c Release --nologo
if errorlevel 1 (
  echo.
  echo [!] Tests failed. Please copy the messages above and send them to me.
  pause
  exit /b 1
)

echo.
echo [*] Closing any running iPlaySpeed instance (avoids file-lock build error)...
taskkill /IM iPlaySpeed.exe /F >nul 2>nul

echo.
echo [2/2] Building and launching the app...
dotnet run --project "src\IPlaySpeed.App\IPlaySpeed.App.csproj" -c Release
echo.
echo (App has exited.)
pause
