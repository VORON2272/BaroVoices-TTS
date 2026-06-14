#!/bin/bash
cd "$(dirname "$0")"

LANG_ARG="$1"
if [ -z "$LANG_ARG" ]; then LANG_ARG="en"; fi

if [ "$LANG_ARG" == "ru" ]; then
    MSG_TITLE="[TTS] Выберите действие"
    MSG_OPT1="1 - Запуск Сервера (Процессор / По умолчанию)"
    MSG_OPT2="2 - Запуск Сервера (Видеокарта NVIDIA)"
    MSG_OPT3="3 - Установка/Починка (Скачивание Python)"
    MSG_PROMPT="Введите 1, 2 или 3 и нажмите Enter: "
    MSG_INVALID="Неверный ввод, выбран вариант 1."
    MSG_STARTING="[TTS] Запуск Сервера..."
    MSG_INST_MODE="[TTS] Выбор версии для установки"
    MSG_INST_CPU="1 - Версия для процессора (Стандарт)"
    MSG_INST_GPU="2 - Версия для видеокарты (Требуется NVIDIA CUDA)"
    MSG_INST_PROMPT="Введите 1 или 2: "
    MSG_PATH_TITLE="[TTS] Путь установки"
    MSG_PATH_1="Куда установить Python и зависимости?"
    MSG_PATH_2="Нажмите ENTER для стандартного пути (внутри папки мода)."
    MSG_PATH_3="Пример другого пути: /home/user/TTS_Env"
    MSG_PATH_PROMPT="Введите путь или нажмите ENTER: "
    MSG_INIT="[TTS] Подготовка среды..."
    MSG_INSTALL_UV="Установка пакетного менеджера uv..."
    MSG_DL_PYTHON="Скачивание портативного Python 3.11..."
    MSG_VENV="Создание изолированной среды Python в "
    MSG_DL_GPU="Установка GPU PyTorch (CUDA)..."
    MSG_DL_CPU="Установка CPU PyTorch..."
    MSG_DONE="[TTS] Установка завершена! Запуск Сервера..."
    MSG_PRESS="Нажмите Enter для выхода..."
else
    MSG_TITLE="[TTS] Select Action"
    MSG_OPT1="1 - Start Server (CPU / Default)"
    MSG_OPT2="2 - Start Server (GPU NVIDIA)"
    MSG_OPT3="3 - Install/Repair Environment (Downloads Python)"
    MSG_PROMPT="Enter 1, 2, or 3 and press Enter: "
    MSG_INVALID="Invalid input, defaulting to 1."
    MSG_STARTING="[TTS] Starting Server..."
    MSG_INST_MODE="[TTS] Installation Mode"
    MSG_INST_CPU="1 - CPU Version (Standard)"
    MSG_INST_GPU="2 - GPU Version (Requires NVIDIA CUDA)"
    MSG_INST_PROMPT="Enter 1 or 2: "
    MSG_PATH_TITLE="[TTS] Installation Path Setup"
    MSG_PATH_1="Where do you want to install Python and dependencies?"
    MSG_PATH_2="Press ENTER to use the default path (inside the mod folder)."
    MSG_PATH_3="Example of custom path: /home/user/TTS_Env"
    MSG_PATH_PROMPT="Enter path or press ENTER: "
    MSG_INIT="[TTS] Initializing Environment..."
    MSG_INSTALL_UV="Installing uv package manager..."
    MSG_DL_PYTHON="Downloading portable Python 3.11 if not installed..."
    MSG_VENV="Creating isolated Python environment at "
    MSG_DL_GPU="Installing GPU PyTorch (CUDA)..."
    MSG_DL_CPU="Installing CPU PyTorch..."
    MSG_DONE="[TTS] Installation Complete! Starting Server..."
    MSG_PRESS="Press Enter to exit..."
fi

echo "=============================================="
echo "$MSG_TITLE"
echo "=============================================="
echo "$MSG_OPT1"
echo "$MSG_OPT2"
echo "$MSG_OPT3"
echo "=============================================="
read -p "$MSG_PROMPT" mode

if [ "$mode" != "1" ] && [ "$mode" != "2" ] && [ "$mode" != "3" ]; then
    echo "$MSG_INVALID"
    mode="1"
fi

if [ "$mode" == "1" ] || [ "$mode" == "2" ]; then
    DEVICE_ARG="cpu"
    if [ "$mode" == "2" ]; then
        DEVICE_ARG="gpu"
    fi
    echo ""
    echo "=============================================="
    echo "$MSG_STARTING"
    echo "=============================================="
    PYTHON_EXE="python3"
    if [ -f "venv_path.txt" ]; then
        SAVED_VENV=$(cat venv_path.txt)
        if [ -f "$SAVED_VENV/bin/python" ]; then
            PYTHON_EXE="$SAVED_VENV/bin/python"
        fi
    elif [ -f ".venv/bin/python" ]; then
        PYTHON_EXE=".venv/bin/python"
    fi
    "$PYTHON_EXE" silero_server.py "$LANG_ARG" "$DEVICE_ARG"
    read -p "$MSG_PRESS"
    exit 0
fi

echo ""
echo "=============================================="
echo "$MSG_INST_MODE"
echo "=============================================="
echo "$MSG_INST_CPU"
echo "$MSG_INST_GPU"
read -p "$MSG_INST_PROMPT" inst_mode

echo ""
echo "=============================================="
echo "$MSG_PATH_TITLE"
echo "=============================================="
echo "$MSG_PATH_1"
echo "$MSG_PATH_2"
echo "$MSG_PATH_3"
read -p "$MSG_PATH_PROMPT" custom_path

VENV_DIR=".venv"
if [ ! -z "$custom_path" ]; then
    mkdir -p "$custom_path"
    VENV_DIR="$custom_path/.venv"
fi
echo "$VENV_DIR" > venv_path.txt

echo ""
echo "=============================================="
echo "$MSG_INIT"
echo "=============================================="

export PATH="$HOME/.local/bin:$HOME/.cargo/bin:$PATH"

if ! command -v uv &> /dev/null; then
    echo "$MSG_INSTALL_UV"
    curl -LsSf https://astral.sh/uv/install.sh | sh
    export PATH="$HOME/.cargo/bin:$PATH"
fi

echo "$MSG_DL_PYTHON"
uv python install 3.11

echo "$MSG_VENV$VENV_DIR..."
uv venv --python 3.11 "$VENV_DIR"

if [ "$inst_mode" == "2" ]; then
    echo "$MSG_DL_GPU"
    uv pip install --python "$VENV_DIR" torch torchaudio --index-url https://download.pytorch.org/whl/cu118
else
    echo "$MSG_DL_CPU"
    uv pip install --python "$VENV_DIR" torch torchaudio --index-url https://download.pytorch.org/whl/cpu
fi

uv pip install --python "$VENV_DIR" soundfile flask omegaconf

echo ""
echo "=============================================="
echo "$MSG_DONE"
echo "=============================================="
SETUP_DEVICE="cpu"
if [ "$inst_mode" == "2" ]; then SETUP_DEVICE="gpu"; fi
"$VENV_DIR/bin/python" silero_server.py "$LANG_ARG" "$SETUP_DEVICE"
read -p "$MSG_PRESS"
