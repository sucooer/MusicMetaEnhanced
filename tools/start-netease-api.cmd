@echo off
rem Starts the local Netease Cloud Music API used by the Emby Apple Music plugin
rem for Chinese artist biographies. Keep this window open while Emby is running.
rem
rem First time setup:
rem   mkdir "%USERPROFILE%\programs\ncm-api" && cd /d "%USERPROFILE%\programs\ncm-api"
rem   npm init -y && npm i @neteasecloudmusicapienhanced/api
rem
rem Then point the plugin at it (EmbyRoot\programdata\plugins\configurations\Emby.Plugin.AppleMusic.xml):
rem   <NeteaseApiBaseUrl>http://127.0.0.1:3055</NeteaseApiBaseUrl>

set PORT=3055
set NCM_DIR=%USERPROFILE%\programs\ncm-api

if not exist "%NCM_DIR%\node_modules\@neteasecloudmusicapienhanced\api\app.js" (
  echo [!] Netease API is not installed in %NCM_DIR%
  echo     Run: npm i @neteasecloudmusicapienhanced/api
  pause
  exit /b 1
)

cd /d "%NCM_DIR%"
echo Starting Netease Cloud Music API on port %PORT% ...
node node_modules\@neteasecloudmusicapienhanced\api\app.js
pause
