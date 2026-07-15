@echo off
setlocal EnableExtensions

rem ---------------------------------------------------------------------------
rem  GAMA Unity Plugin - launcher included with the package (com.project-simple.*)
rem  Expected variables (set by the Unity Editor):
rem    GAMA_HEADLESS_BAT      full path to gama-headless.bat
rem    GAMA_GAML_PATH         full path to the .gaml file
rem    GAMA_BATCH_NAME        experiment name (used by -batch; otherwise -script)
rem    GAMA_JSON_OUTPUT_DIR   output directory for JSON and auto-export scripts
rem    GAMA_HEADLESS_CWD      (optional) working directory before execution
rem    GAMA_HEADLESS_MODE     batch | script | custom  (default: batch)
rem    GAMA_HEADLESS_EXTRA    (optional) additional arguments
rem    GAMA_HEADLESS_CUSTOM   (mode=custom) complete command line to execute after /c
rem ---------------------------------------------------------------------------

if "%GAMA_HEADLESS_BAT%"=="" (
  if /I not "%GAMA_HEADLESS_MODE%"=="custom" (
    echo [GAMA Unity] GAMA_HEADLESS_BAT is missing. 1>&2
    exit /b 10
  )
)
if "%GAMA_GAML_PATH%"=="" (
  if /I not "%GAMA_HEADLESS_MODE%"=="custom" (
    echo [GAMA Unity] GAMA_GAML_PATH is missing. 1>&2
    exit /b 11
  )
)

if not "%GAMA_JSON_OUTPUT_DIR%"=="" (
  set "GAMA_UNITY_JSON_OUT=%GAMA_JSON_OUTPUT_DIR%"
  set "UNITY_GAMA_JSON_EXPORT_DIR=%GAMA_JSON_OUTPUT_DIR%"
)

if not "%GAMA_HEADLESS_BAT%"=="" (
  for %%I in ("%GAMA_HEADLESS_BAT%") do set "GAMA_HEADLESS_DIR=%%~dpI"
)

if not "%GAMA_HEADLESS_CWD%"=="" (
  pushd "%GAMA_HEADLESS_CWD%" 2>nul || (
    echo [GAMA Unity] GAMA_HEADLESS_CWD is invalid: "%GAMA_HEADLESS_CWD%" 1>&2
    exit /b 13
  )
) else if not "%GAMA_HEADLESS_DIR%"=="" (
  pushd "%GAMA_HEADLESS_DIR%" 2>nul || (
    echo [GAMA Unity] Cannot access the headless directory. 1>&2
    exit /b 14
  )
)

set "MODE=%GAMA_HEADLESS_MODE%"
if "%MODE%"=="" set "MODE=batch"

if /I "%MODE%"=="custom" goto :do_custom
if /I "%MODE%"=="script" goto :do_script
goto :do_batch

:do_batch
echo [GAMA Unity] Batch mode: "%GAMA_HEADLESS_BAT%" -batch "%GAMA_BATCH_NAME%" "%GAMA_GAML_PATH%" %GAMA_HEADLESS_EXTRA%
call "%GAMA_HEADLESS_BAT%" -batch "%GAMA_BATCH_NAME%" "%GAMA_GAML_PATH%" %GAMA_HEADLESS_EXTRA%
set RC=%ERRORLEVEL%
goto :done

:do_script
echo [GAMA Unity] Script mode (GUI/Unity experiment): "%GAMA_HEADLESS_BAT%" %GAMA_HEADLESS_EXTRA% "%GAMA_BATCH_NAME%" "%GAMA_GAML_PATH%" "%GAMA_JSON_OUTPUT_DIR%"
if "%GAMA_JSON_OUTPUT_DIR%"=="" (
  call "%GAMA_HEADLESS_BAT%" %GAMA_HEADLESS_EXTRA% "%GAMA_BATCH_NAME%" "%GAMA_GAML_PATH%"
) else (
  call "%GAMA_HEADLESS_BAT%" %GAMA_HEADLESS_EXTRA% "%GAMA_BATCH_NAME%" "%GAMA_GAML_PATH%" "%GAMA_JSON_OUTPUT_DIR%"
)
set RC=%ERRORLEVEL%
goto :done

:do_custom
echo [GAMA Unity] Custom mode: %GAMA_HEADLESS_CUSTOM%
call %GAMA_HEADLESS_CUSTOM%
set RC=%ERRORLEVEL%
goto :done

:done
popd 2>nul
endlocal & exit /b %RC%
