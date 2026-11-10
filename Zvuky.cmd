@echo off
setlocal
cd /d "%~dp0"
python "%~dp0tools\sound-collector\server.py" --open
if errorlevel 1 (
  echo.
  echo Prehled se nepodarilo spustit. Je potreba nainstalovany Python 3.10 nebo novejsi.
  pause
)
