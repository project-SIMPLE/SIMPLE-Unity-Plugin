@echo off
setlocal EnableExtensions

rem ---------------------------------------------------------------------------
rem  GAMA Unity Plugin — lanceur fourni avec le package (com.project-simple.*)
rem  Variables attendues (definies par l'editeur Unity) :
rem    GAMA_HEADLESS_BAT      chemin complet vers gama-headless.bat
rem    GAMA_GAML_PATH         chemin complet du fichier .gaml
rem    GAMA_BATCH_NAME        nom d'experience (utilise par -batch ; sinon -script)
rem    GAMA_JSON_OUTPUT_DIR   dossier de sortie pour les JSON / scripts auto-export
rem    GAMA_HEADLESS_CWD      (optionnel) repertoire de travail avant l'appel
rem    GAMA_HEADLESS_MODE     batch | script | custom  (defaut : batch)
rem    GAMA_HEADLESS_EXTRA    (optionnel) arguments supplementaires
rem    GAMA_HEADLESS_CUSTOM   (mode=custom) ligne complete a executer apres /c
rem ---------------------------------------------------------------------------

if "%GAMA_HEADLESS_BAT%"=="" (
  if /I not "%GAMA_HEADLESS_MODE%"=="custom" (
    echo [GAMA Unity] GAMA_HEADLESS_BAT manquant. 1>&2
    exit /b 10
  )
)
if "%GAMA_GAML_PATH%"=="" (
  if /I not "%GAMA_HEADLESS_MODE%"=="custom" (
    echo [GAMA Unity] GAMA_GAML_PATH manquant. 1>&2
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
    echo [GAMA Unity] GAMA_HEADLESS_CWD invalide : "%GAMA_HEADLESS_CWD%" 1>&2
    exit /b 13
  )
) else if not "%GAMA_HEADLESS_DIR%"=="" (
  pushd "%GAMA_HEADLESS_DIR%" 2>nul || (
    echo [GAMA Unity] Impossible d'acceder au dossier headless. 1>&2
    exit /b 14
  )
)

set "MODE=%GAMA_HEADLESS_MODE%"
if "%MODE%"=="" set "MODE=batch"

if /I "%MODE%"=="custom" goto :do_custom
if /I "%MODE%"=="script" goto :do_script
goto :do_batch

:do_batch
echo [GAMA Unity] Mode batch : "%GAMA_HEADLESS_BAT%" -batch "%GAMA_BATCH_NAME%" "%GAMA_GAML_PATH%" %GAMA_HEADLESS_EXTRA%
call "%GAMA_HEADLESS_BAT%" -batch "%GAMA_BATCH_NAME%" "%GAMA_GAML_PATH%" %GAMA_HEADLESS_EXTRA%
set RC=%ERRORLEVEL%
goto :done

:do_script
echo [GAMA Unity] Mode script (experiment GUI/Unity) : "%GAMA_HEADLESS_BAT%" %GAMA_HEADLESS_EXTRA% "%GAMA_BATCH_NAME%" "%GAMA_GAML_PATH%" "%GAMA_JSON_OUTPUT_DIR%"
if "%GAMA_JSON_OUTPUT_DIR%"=="" (
  call "%GAMA_HEADLESS_BAT%" %GAMA_HEADLESS_EXTRA% "%GAMA_BATCH_NAME%" "%GAMA_GAML_PATH%"
) else (
  call "%GAMA_HEADLESS_BAT%" %GAMA_HEADLESS_EXTRA% "%GAMA_BATCH_NAME%" "%GAMA_GAML_PATH%" "%GAMA_JSON_OUTPUT_DIR%"
)
set RC=%ERRORLEVEL%
goto :done

:do_custom
echo [GAMA Unity] Mode custom : %GAMA_HEADLESS_CUSTOM%
call %GAMA_HEADLESS_CUSTOM%
set RC=%ERRORLEVEL%
goto :done

:done
popd 2>nul
endlocal & exit /b %RC%
