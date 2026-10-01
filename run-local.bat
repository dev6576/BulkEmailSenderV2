@echo off
setlocal

set "ROOT=%~dp0"
set "API_DIR=%ROOT%BulkEmailSender\src\BulkEmailSender.Api"
set "API_PROJECT=%API_DIR%\BulkEmailSender.Api.csproj"
set "WEB_DIR=%ROOT%BulkEmailSenderFrontend"

echo Checking local prerequisites...
where dotnet >nul 2>&1
if errorlevel 1 (
  echo ERROR: .NET 8 SDK was not found. Install the .NET 8 SDK and run this file again.
  pause
  exit /b 1
)
where node >nul 2>&1
if errorlevel 1 (
  echo ERROR: Node.js was not found. Install Node.js and run this file again.
  pause
  exit /b 1
)
where npm >nul 2>&1
if errorlevel 1 (
  echo ERROR: npm was not found. Install Node.js with npm and run this file again.
  pause
  exit /b 1
)
dotnet ef --version >nul 2>&1
if errorlevel 1 (
  echo ERROR: The EF Core command-line tool is missing.
  echo Install it with: dotnet tool install --global dotnet-ef --version 8.0.20
  pause
  exit /b 1
)

echo Checking Google OAuth configuration...
set "HAS_CLIENT_ID="
set "HAS_CLIENT_SECRET="
dotnet user-secrets list --project "%API_PROJECT%" 2>nul | findstr /b /c:"GoogleOAuth:ClientId =" >nul
if not errorlevel 1 set "HAS_CLIENT_ID=1"
dotnet user-secrets list --project "%API_PROJECT%" 2>nul | findstr /b /c:"GoogleOAuth:ClientSecret =" >nul
if not errorlevel 1 set "HAS_CLIENT_SECRET=1"
if not defined HAS_CLIENT_ID if not defined GoogleOAuth__ClientId goto :missing_oauth
if not defined HAS_CLIENT_SECRET if not defined GoogleOAuth__ClientSecret goto :missing_oauth

if not exist "%WEB_DIR%\node_modules\.bin\ng.cmd" (
  echo Installing frontend dependencies...
  pushd "%WEB_DIR%"
  call npm ci
  if errorlevel 1 (
    popd
    echo ERROR: npm ci failed.
    pause
    exit /b 1
  )
  popd
)

echo Applying database migrations...
pushd "%API_DIR%"
dotnet ef database update --project "%API_PROJECT%" --startup-project "%API_PROJECT%"
if errorlevel 1 (
  popd
  echo ERROR: Database migration failed. The app was not started.
  pause
  exit /b 1
)
popd

echo Starting backend and frontend in separate command windows...
start "BulkEmailSender API" /D "%API_DIR%" cmd /k "dotnet run --launch-profile http"
start "BulkEmailSender Angular" /D "%WEB_DIR%" cmd /k "npm start"
timeout /t 8 /nobreak >nul
start "" "http://localhost:4200"
echo BulkEmailSender should open at http://localhost:4200.
echo Keep both command windows open while using the app.
exit /b 0

:missing_oauth
echo ERROR: Google OAuth ClientId or ClientSecret is missing.
echo Follow BulkEmailSender\GOOGLE-OAUTH-SETUP.md, then run this file again.
pause
exit /b 1
