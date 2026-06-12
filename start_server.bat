@echo off
chcp 65001 >nul
cd /d "%~dp0"

set "LANG=%~1"
if "%LANG%"=="" set "LANG=en"

if "%LANG%"=="ru" goto lang_ru
goto lang_en

:lang_ru
set "MSG_TITLE=[TTS] Выберите действие"
set "MSG_OPT1=1 - Запуск Сервера (Процессор / По умолчанию)"
set "MSG_OPT2=2 - Запуск Сервера (Видеокарта NVIDIA)"
set "MSG_OPT3=3 - Установка/Починка (Скачивание Python)"
set "MSG_PROMPT=Введите 1, 2 или 3 и нажмите Enter: "
set "MSG_INVALID=Неверный ввод, выбран вариант 1."
set "MSG_STARTING=[TTS] Запуск Сервера..."
set "MSG_INST_MODE=[TTS] Выбор версии для установки"
set "MSG_INST_CPU=1 - Версия для процессора (Стандарт)"
set "MSG_INST_GPU=2 - Версия для видеокарты (Требуется NVIDIA CUDA)"
set "MSG_INST_PROMPT=Введите 1 или 2: "
set "MSG_PATH_TITLE=[TTS] Путь установки"
set "MSG_PATH_1=Куда установить Python и зависимости?"
set "MSG_PATH_2=Нажмите ENTER для стандартного пути (внутри папки мода)."
set "MSG_PATH_3=Пример другого пути: D:\TTS_Env"
set "MSG_PATH_PROMPT=Введите путь или нажмите ENTER: "
set "MSG_INIT=[TTS] Подготовка среды..."
set "MSG_INSTALL_UV=Установка пакетного менеджера uv..."
set "MSG_DL_PYTHON=Скачивание портативного Python 3.11..."
set "MSG_VENV=Создание изолированной среды Python в "
set "MSG_DL_GPU=Установка GPU PyTorch (CUDA)..."
set "MSG_DL_CPU=Установка CPU PyTorch..."
set "MSG_DONE=[TTS] Установка завершена! Запуск Сервера..."
goto lang_done

:lang_en
set "MSG_TITLE=[TTS] Select Action"
set "MSG_OPT1=1 - Start Server (CPU / Default)"
set "MSG_OPT2=2 - Start Server (GPU NVIDIA)"
set "MSG_OPT3=3 - Install/Repair Environment (Downloads Python)"
set "MSG_PROMPT=Enter 1, 2, or 3 and press Enter: "
set "MSG_INVALID=Invalid input, defaulting to 1."
set "MSG_STARTING=[TTS] Starting Server..."
set "MSG_INST_MODE=[TTS] Installation Mode"
set "MSG_INST_CPU=1 - CPU Version (Standard)"
set "MSG_INST_GPU=2 - GPU Version (Requires NVIDIA CUDA)"
set "MSG_INST_PROMPT=Enter 1 or 2: "
set "MSG_PATH_TITLE=[TTS] Installation Path Setup"
set "MSG_PATH_1=Where do you want to install Python and dependencies?"
set "MSG_PATH_2=Press ENTER to use the default path (inside the mod folder)."
set "MSG_PATH_3=Example of custom path: D:\TTS_Env"
set "MSG_PATH_PROMPT=Enter path or press ENTER: "
set "MSG_INIT=[TTS] Initializing Environment..."
set "MSG_INSTALL_UV=Installing uv package manager..."
set "MSG_DL_PYTHON=Downloading portable Python 3.11 if not installed..."
set "MSG_VENV=Creating isolated Python environment at "
set "MSG_DL_GPU=Installing GPU PyTorch (CUDA)..."
set "MSG_DL_CPU=Installing CPU PyTorch..."
set "MSG_DONE=[TTS] Installation Complete! Starting Server..."
goto lang_done

:lang_done

echo ==============================================
echo %MSG_TITLE%
echo ==============================================
echo %MSG_OPT1%
echo %MSG_OPT2%
echo %MSG_OPT3%
echo ==============================================
set /p mode="%MSG_PROMPT%"

if "%mode%"=="1" (
    set "DEVICE_ARG=cpu"
    goto run
)
if "%mode%"=="2" (
    set "DEVICE_ARG=gpu"
    goto run
)
if "%mode%"=="3" goto setup

echo %MSG_INVALID%
set "DEVICE_ARG=cpu"
goto run

:run
echo.
echo ==============================================
echo %MSG_STARTING%
echo ==============================================
set "PYTHON_EXE=python"
if exist "venv_path.txt" set /p SAVED_VENV=<venv_path.txt
if defined SAVED_VENV (
    if exist "%SAVED_VENV%\Scripts\python.exe" set "PYTHON_EXE=%SAVED_VENV%\Scripts\python.exe"
) else (
    if exist ".venv\Scripts\python.exe" set "PYTHON_EXE=.venv\Scripts\python.exe"
)
"%PYTHON_EXE%" Server\silero_server.py %LANG% %DEVICE_ARG%
pause
exit

:setup
echo.
echo ==============================================
echo %MSG_INST_MODE%
echo ==============================================
echo %MSG_INST_CPU%
echo %MSG_INST_GPU%
set /p inst_mode="%MSG_INST_PROMPT%"

echo.
echo ==============================================
echo %MSG_PATH_TITLE%
echo ==============================================
echo %MSG_PATH_1%
echo %MSG_PATH_2%
echo %MSG_PATH_3%
set /p custom_path="%MSG_PATH_PROMPT%"

set "VENV_DIR=.venv"
if not "%custom_path%"=="" (
    if not exist "%custom_path%" mkdir "%custom_path%"
    set "VENV_DIR=%custom_path%\.venv"
)
echo %VENV_DIR%> venv_path.txt

echo.
echo ==============================================
echo %MSG_INIT%
echo ==============================================

set "PATH=%USERPROFILE%\.local\bin;%USERPROFILE%\.cargo\bin;%PATH%"
where uv >nul 2>nul
if %errorlevel% neq 0 (
    echo %MSG_INSTALL_UV%
    powershell -ExecutionPolicy ByPass -c "irm https://astral.sh/uv/install.ps1 | iex"
)

echo %MSG_DL_PYTHON%
uv python install 3.11

echo %MSG_VENV%%VENV_DIR%...
uv venv --python 3.11 "%VENV_DIR%"

if "%inst_mode%"=="2" goto dl_gpu
goto dl_cpu

:dl_gpu
echo %MSG_DL_GPU%
uv pip install --python "%VENV_DIR%" torch torchaudio --index-url https://download.pytorch.org/whl/cu118
goto dl_done

:dl_cpu
echo %MSG_DL_CPU%
uv pip install --python "%VENV_DIR%" torch torchaudio --index-url https://download.pytorch.org/whl/cpu
goto dl_done

:dl_done
uv pip install --python "%VENV_DIR%" soundfile flask omegaconf

echo.
echo ==============================================
echo %MSG_DONE%
echo ==============================================
set "SETUP_DEVICE=cpu"
if "%inst_mode%"=="2" set "SETUP_DEVICE=gpu"
"%VENV_DIR%\Scripts\python.exe" Server\silero_server.py %LANG% %SETUP_DEVICE%
pause
exit
