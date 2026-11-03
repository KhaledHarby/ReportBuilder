@echo off
setlocal EnableExtensions
set "ROOT=%~dp0"
cd /d "%ROOT%"

echo === MECS Report Builder dev servers ===
echo Project: %ROOT%
echo.

where node >nul 2>&1
if errorlevel 1 (
  echo Node.js is not on PATH.
  goto :fail
)
where npm >nul 2>&1
if errorlevel 1 (
  echo npm is not on PATH.
  goto :fail
)
where dotnet >nul 2>&1
if errorlevel 1 (
  echo The .NET SDK is not on PATH.
  goto :fail
)
if not exist "frontend\package.json" (
  echo frontend\package.json was not found. Run this file from the report-builder folder.
  goto :fail
)
if not exist "backend\src\ReportBuilder.Api\ReportBuilder.Api.csproj" (
  echo backend\src\ReportBuilder.Api\ReportBuilder.Api.csproj was not found.
  goto :fail
)

call :stop_dev_servers

where sqllocaldb >nul 2>&1
if not errorlevel 1 (
  echo Starting LocalDB...
  sqllocaldb start MSSQLLocalDB >nul 2>&1
)

if not exist "frontend\node_modules\" (
  echo Installing npm packages...
  pushd "frontend"
  call npm install
  if errorlevel 1 (
    popd
    goto :fail
  )
  popd
)

set "API_DIR=%ROOT%backend\src\ReportBuilder.Api"
set "UI_DIR=%ROOT%frontend"

echo.
echo Starting API on http://localhost:5244 ...
start "Report Builder API" cmd /k "cd /d ""%API_DIR%"" && dotnet run --launch-profile http"

echo Starting UI on http://localhost:5173 ...
start "Report Builder UI" cmd /k "cd /d ""%UI_DIR%"" && npm run dev"

echo.
echo Dev servers are starting in separate windows.
echo   API: http://localhost:5244
echo   UI:  http://localhost:5173
echo.
echo Close the API and UI windows to stop the servers.
pause
exit /b 0

:stop_dev_servers
echo.
echo Stopping any existing Report Builder dev servers...

rem Close dev server console windows started by this script
taskkill /FI "WINDOWTITLE eq Report Builder API*" /F >nul 2>&1
taskkill /FI "WINDOWTITLE eq Report Builder UI*" /F >nul 2>&1

rem Stop listeners on dev ports (API + Vite)
call :kill_port 5244
call :kill_port 5173

rem Stop dotnet hosts for this API project (releases locked DLLs)
for /f "skip=1 tokens=2 delims=," %%p in ('wmic process where "name='dotnet.exe' and CommandLine like '%%ReportBuilder.Api%%'" get ProcessId /format:csv 2^>nul') do (
  if not "%%p"=="" (
    echo   Stopping dotnet PID %%p
    taskkill /F /PID %%p >nul 2>&1
  )
)

rem Stop node/vite dev server for this frontend
for /f "skip=1 tokens=2 delims=," %%p in ('wmic process where "name='node.exe' and CommandLine like '%%report-builder\frontend%%'" get ProcessId /format:csv 2^>nul') do (
  if not "%%p"=="" (
    echo   Stopping node PID %%p
    taskkill /F /PID %%p >nul 2>&1
  )
)

timeout /t 1 /nobreak >nul
echo.
exit /b 0

:kill_port
set "PORT=%~1"
for /f "tokens=5" %%p in ('netstat -ano ^| findstr ":%PORT%" ^| findstr "LISTENING"') do (
  if not "%%p"=="0" (
    echo   Stopping process on port %PORT% ^(PID %%p^)
    taskkill /F /PID %%p >nul 2>&1
  )
)
exit /b 0

:fail
echo.
echo Dev startup failed.
pause
exit /b 1
