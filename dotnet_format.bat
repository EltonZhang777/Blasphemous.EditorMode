@echo off
setlocal EnableExtensions DisableDelayedExpansion

pushd "%~dp0"
if errorlevel 1 (
    echo Failed to enter the script directory.
    exit /b 1
)

where dotnet >nul 2>&1
if errorlevel 1 (
    echo The dotnet command was not found on PATH.
    set "exitCode=1"
    goto :finish
)

set "formatTarget=%~1"
if defined formatTarget goto :targetReady

set "solutionCount=0"
for %%F in (*.sln) do if exist "%%~fF" (
    set /a solutionCount+=1 >nul
    set "formatTarget=%%~fF"
)
if %solutionCount% GTR 1 (
    echo Multiple solution files found. Pass the solution or project path as the first argument.
    set "exitCode=2"
    goto :finish
)
if %solutionCount% EQU 1 goto :targetReady

set "projectCount=0"
for %%F in (*.csproj) do if exist "%%~fF" (
    set /a projectCount+=1 >nul
    set "formatTarget=%%~fF"
)
if %projectCount% GTR 1 (
    echo Multiple project files found and no solution file was found. Pass the project path as the first argument.
    set "exitCode=2"
    goto :finish
)
if not defined formatTarget (
    echo No solution or project file was found next to this script.
    set "exitCode=2"
    goto :finish
)

:targetReady
if not exist "%formatTarget%" (
    echo Format target not found: "%formatTarget%"
    set "exitCode=2"
    goto :finish
)

echo Formatting "%formatTarget%"...
dotnet restore "%formatTarget%"
if errorlevel 1 goto :failed

dotnet format whitespace "%formatTarget%" --no-restore
if errorlevel 1 goto :failed

rem First apply warning/error style fixes and import formatting.
dotnet format style "%formatTarget%" --no-restore --severity warn
if errorlevel 1 goto :failed

rem Discover info-level diagnostics from the actual workspace.
set "formatTempDirectory=%TEMP%\dotnet-format-%RANDOM%-%RANDOM%"
if exist "%formatTempDirectory%" (
    echo Temporary format directory already exists: "%formatTempDirectory%"
    set "exitCode=1"
    goto :finish
)
md "%formatTempDirectory%"
if errorlevel 1 (
    echo Failed to create temporary format directory: "%formatTempDirectory%"
    set "exitCode=1"
    goto :finish
)
set "formatTempOwned=1"
set "formatReportPath=%formatTempDirectory%\info-report.json"
set "infoDiagnosticListPath=%formatTempDirectory%\info-diagnostics.txt"

dotnet format style "%formatTarget%" --no-restore --severity info --verify-no-changes --report "%formatReportPath%"
set "discoveryExitCode=%errorlevel%"
if not exist "%formatReportPath%" (
    echo Failed to discover info diagnostics. dotnet format exited with %discoveryExitCode%.
    set "exitCode=%discoveryExitCode%"
    if "%exitCode%"=="0" set "exitCode=1"
    goto :finish
)
if not "%discoveryExitCode%"=="0" if not "%discoveryExitCode%"=="2" (
    echo ERROR: info discovery failed with %discoveryExitCode%.
    set "exitCode=%discoveryExitCode%"
    goto :finish
)
if "%discoveryExitCode%"=="2" echo Info discovery found formatting changes; exit code 2 is expected with --verify-no-changes; using the generated report.

rem The report has no code-fix metadata. The subroutine below classifies
rem "No associated code fix found" as a skipped, non-failing diagnostic.
rem IDE0060 has no associated code fix and is intentionally omitted.
powershell.exe -NoProfile -NonInteractive -Command "$report = Get-Content -Raw -LiteralPath $env:formatReportPath | ConvertFrom-Json; $ids = @($report | ForEach-Object { $_.FileChanges } | Where-Object { $_.DiagnosticId -and $_.DiagnosticId -notin @('IDE0060', 'IDE0130', 'IMPORTS') } | Select-Object -ExpandProperty DiagnosticId -Unique | Sort-Object); [System.IO.File]::WriteAllLines($env:infoDiagnosticListPath, [string[]]$ids, [System.Text.Encoding]::ASCII)"
if errorlevel 1 (
    echo Failed to parse the info diagnostic report.
    set "exitCode=1"
    goto :finish
)

set "infoFormatFailed=0"
for /f "usebackq delims=" %%D in ("%infoDiagnosticListPath%") do (
    call :formatInfoDiagnostic "%%D"
    if errorlevel 1 set "infoFormatFailed=1"
)

if "%infoFormatFailed%"=="1" (
    echo Info formatting completed with one or more failures.
    set "exitCode=1"
) else (
    set "exitCode=0"
)
goto :finish

:failed
set "exitCode=%errorlevel%"

:finish
if defined formatTempOwned if "%exitCode%"=="0" (
    rmdir /s /q "%formatTempDirectory%" >nul 2>&1
)
if defined formatTempOwned if not "%exitCode%"=="0" (
    echo Diagnostic report retained at "%formatTempDirectory%".
)
popd
exit /b %exitCode%

:formatInfoDiagnostic
set "diagnosticId=%~1"
set "diagnosticLogPath=%formatTempDirectory%\%diagnosticId%.log"

echo Applying info diagnostic %diagnosticId%...
dotnet format style "%formatTarget%" --no-restore --severity info --diagnostics %diagnosticId% > "%diagnosticLogPath%" 2>&1
set "diagnosticExitCode=%errorlevel%"
type "%diagnosticLogPath%"

findstr /i /c:"No associated code fix found" "%diagnosticLogPath%" >nul
if not errorlevel 1 (
    echo Skipped %diagnosticId%: no associated code fix found.
    exit /b 0
)
if not "%diagnosticExitCode%"=="0" (
    echo ERROR: info diagnostic %diagnosticId% failed; continuing.
    exit /b 1
)
exit /b 0
