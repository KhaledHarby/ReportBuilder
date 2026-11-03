@echo off
setlocal EnableExtensions
set "ROOT=%~dp0"
cd /d "%ROOT%"

echo === MECS Report Builder production release ===
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

echo [1/4] Building frontend...
pushd "frontend"
if not exist "node_modules\" (
  echo Installing npm packages...
  call npm install
  if errorlevel 1 (
    popd
    goto :fail
  )
)
call npm run build
if errorlevel 1 (
  popd
  goto :fail
)
popd
if not exist "frontend\dist\index.html" (
  echo Frontend build did not produce frontend\dist\index.html.
  goto :fail
)

echo.
echo [2/4] Publishing API...
dotnet publish "backend\src\ReportBuilder.Api\ReportBuilder.Api.csproj" -c Release -o "%ROOT%publish"
if errorlevel 1 goto :fail

echo.
echo [3/4] Applying database schema migrations...
pushd "%ROOT%publish"
set "ASPNETCORE_ENVIRONMENT=Production"
dotnet "ReportBuilder.Api.dll" --migrate-only
if errorlevel 1 (
  popd
  goto :fail
)
popd

echo.
echo [4/4] Copying UI into publish\wwwroot...
if exist "%ROOT%publish\wwwroot\" rmdir /s /q "%ROOT%publish\wwwroot"
mkdir "%ROOT%publish\wwwroot"
robocopy "%ROOT%frontend\dist" "%ROOT%publish\wwwroot" /E /NFL /NDL /NJH /NJS /NC /NS /NP
if errorlevel 8 goto :fail
if not exist "%ROOT%publish\logs\" mkdir "%ROOT%publish\logs"
if not exist "%ROOT%publish\wwwroot\index.html" (
  echo publish\wwwroot\index.html is missing.
  goto :fail
)
if not exist "%ROOT%publish\ReportBuilder.Api.dll" (
  echo publish\ReportBuilder.Api.dll is missing.
  goto :fail
)

echo.
echo Release ready: %ROOT%publish
echo Copy that folder to the IIS site and recycle the app pool.
echo.
pause
exit /b 0

:fail
echo.
echo Release failed.
pause
exit /b 1
