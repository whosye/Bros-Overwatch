@echo off
setlocal EnableExtensions DisableDelayedExpansion
title Bros Overwatch - Build

rem Works next to the repository, or from the repository root.
set "PROJECT=%~dp0Bros-Overwatch\OverwatchBros"
if not exist "%PROJECT%\ProjectSettings\ProjectVersion.txt" set "PROJECT=%~dp0OverwatchBros"
set "OUTPUT=%~dp0compiled game"
set "BUILD_LOG=%~dp0compile-game.log"

if not exist "%PROJECT%\ProjectSettings\ProjectVersion.txt" (
    echo ERROR: Cannot find the OverwatchBros Unity project next to this script.
    goto :failed
)

set "UNITY_VERSION="
for /f "tokens=2" %%V in ('findstr /b /c:"m_EditorVersion:" "%PROJECT%\ProjectSettings\ProjectVersion.txt"') do set "UNITY_VERSION=%%V"
if not defined UNITY_VERSION (
    echo ERROR: Cannot read the Unity version from ProjectVersion.txt.
    goto :failed
)
set "UNITY_EXE=%ProgramFiles%\Unity\Hub\Editor\%UNITY_VERSION%\Editor\Unity.exe"
if not exist "%UNITY_EXE%" (
    echo ERROR: Unity %UNITY_VERSION% is not installed in the default Hub location.
    echo Expected: "%UNITY_EXE%"
    goto :failed
)

echo Unity:   %UNITY_VERSION%
echo Project: "%PROJECT%"
echo Output:  "%OUTPUT%"
echo Log:     "%BUILD_LOG%"
if /i "%~1"=="--check" exit /b 0
echo.
echo Close this project in Unity Editor before building.
echo Building the Windows game. This can take several minutes...
echo.

start "" /b /wait "%UNITY_EXE%" -batchmode -quit -projectPath "%PROJECT%" -buildTarget StandaloneWindows64 -executeMethod MenuPlaytest.BuildGame -brosBuildOutput "%OUTPUT%" -logFile "%BUILD_LOG%"
if errorlevel 1 goto :buildFailed
if not exist "%OUTPUT%\BrosOverwatch.exe" goto :buildFailed

echo.
echo SUCCESS: "%OUTPUT%\BrosOverwatch.exe"
echo To share the game, copy the entire "compiled game" folder.
if /i not "%~1"=="--no-pause" pause
exit /b 0

:buildFailed
echo.
echo ERROR: The build failed. See "%BUILD_LOG%" for details.
:failed
if /i not "%~1"=="--no-pause" pause
exit /b 1
