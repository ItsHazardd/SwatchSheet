@echo off
setlocal
cd /d "%~dp0"
set /p VER=<VERSION
rem Builds go in "SwatchSheet Builds" next to this folder if it exists, so nothing
rem personal (saved sheets, logs) ever ends up in the source folder.
set OUT=release\Swatch Sheet %VER%
if exist "..\SwatchSheet Builds\" set OUT=..\SwatchSheet Builds\release\Swatch Sheet %VER%

echo.
echo === Swatch Sheet %VER% ===
echo.
echo Running tests...
dotnet run -c Release --project tests\SwatchSheet.Tests
if errorlevel 1 (
  echo.
  echo TESTS FAILED - not building.
  pause
  exit /b 1
)

rem The folder is NOT wiped first: it may hold saved colors and palettes.
rem Publishing just replaces SwatchSheet.exe.

echo.
echo Building SwatchSheet.exe...
dotnet publish src\SwatchSheet\SwatchSheet.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o "%OUT%"
if errorlevel 1 goto failed

copy /y README.txt "%OUT%\README.txt" >nul

rem A zip with just the program and README (never anyone's saved colors), ready to send,
rem plus its SHA-256 fingerprint. Attach BOTH to the GitHub release: the in-app updater
rem won't install a zip whose fingerprint doesn't match.
set ZIP=%OUT%\..\SwatchSheet-%VER%.zip
echo.
echo Making SwatchSheet-%VER%.zip and its fingerprint...
powershell -NoProfile -Command "$ErrorActionPreference='Stop'; Compress-Archive -Path '%OUT%\SwatchSheet.exe','%OUT%\README.txt' -DestinationPath '%ZIP%' -Force; $h=(Get-FileHash '%ZIP%' -Algorithm SHA256).Hash; [IO.File]::WriteAllText('%ZIP%.sha256', $h + '  SwatchSheet-%VER%.zip' + [char]10)"
if errorlevel 1 goto failed

echo.
echo Done: %OUT%
pause
exit /b 0

:failed
echo.
echo BUILD FAILED.
pause
exit /b 1
