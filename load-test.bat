@echo off
setlocal EnableExtensions EnableDelayedExpansion
title Report Builder load test
cd /d "%~dp0"

echo.
echo Report Builder load test
echo Each user signs in once, then runs the report that many times in parallel with the other users.
echo Local API: http://localhost:5244
echo Production site: https://mecs.dmt.gov.ae/ReportBuilder
echo A report page address is fine. The test calls /api on that site.
echo.

set "BASE=http://localhost:5244"
set /p BASE=API address [%BASE%]:
set /p REPORT=Report id:
set /p USERNAME=Username:
set /p USERS=How many users:
set /p EACH=How many requests per user:

if "%REPORT%"=="" (
  echo Report id is required.
  goto :done
)
if "%USERNAME%"=="" (
  echo Username is required.
  goto :done
)
if "%USERS%"=="" set "USERS=1"
if "%EACH%"=="" set "EACH=1"

echo %BASE% | findstr /i "mecs.dmt.gov.ae" >nul
if not errorlevel 1 (
  echo.
  echo That address is production.
  set /p CONFIRM=Type YES to run the test there: 
  if /I not "!CONFIRM!"=="YES" (
    echo Stopped.
    goto :done
  )
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0load-test.ps1" -BaseUrl "%BASE%" -ReportId "%REPORT%" -UserName "%USERNAME%" -Users %USERS% -Requests %EACH%
set "EXITCODE=%ERRORLEVEL%"
echo.
if not "%EXITCODE%"=="0" echo Load test failed. Exit code %EXITCODE%.

:done
echo.
pause
endlocal
