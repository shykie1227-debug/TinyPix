@echo off
cd /d %~dp0

echo ========================================
echo TinyPix Pro 3.5.1 Windows Build
echo ========================================
echo.
echo Working directory:
cd
echo.

python --version
if errorlevel 1 (
    echo.
    echo ERROR: Python was not found.
    echo Install Python 3.10 or newer, then run this file again.
    echo https://www.python.org/downloads/windows/
    echo.
    pause
    exit /b 1
)

echo.
echo Running build.py ...
echo.
python build.py
set BUILD_RESULT=%errorlevel%

echo.
echo ========================================
if %BUILD_RESULT% equ 0 (
    echo Build command finished. Check logs\build_info.json for the EXE path.
) else (
    echo Build failed. Send logs\error.log and logs\build.log for diagnosis.
)
echo ========================================
echo.
pause
exit /b %BUILD_RESULT%
