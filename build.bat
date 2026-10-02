@echo off
REM Minimal build: compiles the DLL and drops it into GameData\KSPDiagColliders\.
REM Installing means copying that folder into the GameData of KSP -- this script never does it.
setlocal
cd /d "%~dp0"

if not defined KSPDIR (
    echo ERROR: KSPDIR is not set. Point it at your KSP install folder.
    exit /b 1
)

dotnet build KSPDiagColliders.csproj -p:KSP_DATA_DIR="%KSPDIR%\KSP_x64_Data"
if errorlevel 1 (
    echo ERROR: build failed
    exit /b 1
)

copy /y "Output\bin\KSPDiagColliders.dll" "GameData\KSPDiagColliders\" >nul
if errorlevel 1 (
    echo ERROR: could not copy the DLL into GameData
    exit /b 1
)

echo.
echo Built: GameData\KSPDiagColliders\KSPDiagColliders.dll
echo Copy GameData\KSPDiagColliders into the GameData of KSP to install it.
