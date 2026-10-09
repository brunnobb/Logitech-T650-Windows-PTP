@echo off
setlocal
set SCRIPTDIR=%~dp0
pushd "%SCRIPTDIR%"

echo ==========================================================
echo  Logitech T650 Virtual PTP Driver Installation
echo ==========================================================

echo [1/4] Installing Test Signing Certificate...
certutil.exe -addstore "TrustedPublisher" "%SCRIPTDIR%T650TestCert.cer" >nul 2>&1
certutil.exe -addstore "Root" "%SCRIPTDIR%T650TestCert.cer" >nul 2>&1

echo [2/4] Configuring Windows testsigning...
bcdedit.exe /set testsigning on >nul 2>&1

echo [3/4] Adding driver to Windows DriverStore via pnputil...
pnputil.exe /add-driver "%SCRIPTDIR%VirtualPtpDriver.inf" /install >nul 2>&1

echo [4/4] Creating Virtual PTP root device node via devcon...
if exist "%SCRIPTDIR%devcon.exe" (
    "%SCRIPTDIR%devcon.exe" install "%SCRIPTDIR%VirtualPtpDriver.inf" "Root\T650VirtualPtp" >nul 2>&1
)

popd
endlocal
exit /b 0
