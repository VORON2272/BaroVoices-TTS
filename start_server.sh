#!/bin/bash
cd "$(dirname "$0")"

LANG_ARG="$1"
if [ -z "$LANG_ARG" ]; then LANG_ARG="en"; fi

OS_NAME="$(uname -s)"
ARCH_NAME="$(uname -m)"

if [ "$LANG_ARG" == "ru" ]; then
    MSG_TITLE="[TTS] Выберите действие"
    MSG_OPT1="1 - Запуск Сервера (Процессор / По умолчанию)"
    if [ "$OS_NAME" == "Darwin" ]; then
        MSG_OPT2="2 - Запуск Сервера (Apple Silicon MPS / Metal)"
    else
        MSG_OPT2="2 - Запуск Сервера (Видеокарта NVIDIA CUDA)"
    fi
    MSG_OPT3="3 - Установка/Починка (Скачивание Python)"
    MSG_PROMPT="Введите 1, 2 или 3 и нажмите Enter: "
    MSG_INVALID="Неверный ввод, выбран вариант 1."
    MSG_STARTING="[TTS] Запуск Сервера..."
    MSG_INST_MODE="[TTS] Выбор версии для установки"
    MSG_INST_CPU="1 - Версия для процессора (Стандарт)"
    if [ "$OS_NAME" == "Darwin" ]; then
        MSG_INST_GPU="2 - Версия для Apple Silicon (M1/M2/M3/M4 Metal Ускорение)"
    else
        MSG_INST_GPU="2 - Версия для видеокарты (Требуется NVIDIA CUDA)"
    fi
    MSG_INST_PROMPT="Введите 1 или 2: "
    MSG_PATH_TITLE="[TTS] Путь установки"
    MSG_PATH_1="Куда установить Python и зависимости?"
    MSG_PATH_2="Нажмите ENTER для стандартного пути (внутри папки мода)."
    MSG_PATH_3="Пример другого пути: $HOME/TTS_Env"
    MSG_PATH_PROMPT="Введите путь или нажмите ENTER: "
    MSG_INIT="[TTS] Подготовка среды..."
    MSG_INSTALL_UV="Установка пакетного менеджера uv..."
    MSG_DL_PYTHON="Скачивание портативного Python 3.11..."
    MSG_VENV="Создание изолированной среды Python в "
    if [ "$OS_NAME" == "Darwin" ]; then
        MSG_DL_GPU="Установка PyTorch с поддержкой Apple Silicon (MPS)..."
    else
        MSG_DL_GPU="Установка GPU PyTorch (CUDA)..."
    fi
    MSG_DL_CPU="Установка CPU PyTorch..."
    MSG_DONE="[TTS] Установка завершена! Запуск Сервера..."
    MSG_PRESS="Нажмите Enter для выхода..."
elif [ "$LANG_ARG" == "zh" ]; then
    MSG_TITLE="[TTS] 选择操作"
    MSG_OPT1="1 - 启动服务器 (CPU / 默认)"
    if [ "$OS_NAME" == "Darwin" ]; then
        MSG_OPT2="2 - 启动服务器 (Apple Silicon MPS / Metal)"
    else
        MSG_OPT2="2 - 启动服务器 (NVIDIA GPU)"
    fi
    MSG_OPT3="3 - 安装/修复环境 (下载Python)"
    MSG_PROMPT="输入 1, 2 或 3 并按回车: "
    MSG_INVALID="输入无效，默认为 1。"
    MSG_STARTING="[TTS] 正在启动服务器..."
    MSG_INST_MODE="[TTS] 安装模式"
    MSG_INST_CPU="1 - CPU 版本 (标准)"
    if [ "$OS_NAME" == "Darwin" ]; then
        MSG_INST_GPU="2 - Apple Silicon 版本 (M1/M2/M3/M4 Metal 加速)"
    else
        MSG_INST_GPU="2 - GPU 版本 (需要 NVIDIA CUDA)"
    fi
    MSG_INST_PROMPT="输入 1 或 2: "
    MSG_PATH_TITLE="[TTS] 安装路径设置"
    MSG_PATH_1="你想在哪里安装 Python 及其依赖？"
    MSG_PATH_2="按 ENTER 使用默认路径 (在模组文件夹内)。"
    MSG_PATH_3="自定义路径示例：$HOME/TTS_Env"
    MSG_PATH_PROMPT="输入路径或按 ENTER: "
    MSG_INIT="[TTS] 正在初始化环境..."
    MSG_INSTALL_UV="正在安装 uv 包管理器..."
    MSG_DL_PYTHON="如果没有安装，正在下载便携版 Python 3.11..."
    MSG_VENV="正在创建隔离的 Python 环境于 "
    if [ "$OS_NAME" == "Darwin" ]; then
        MSG_DL_GPU="正在安装适用于 Apple Silicon 的 PyTorch (MPS)..."
    else
        MSG_DL_GPU="正在安装 GPU PyTorch (CUDA)..."
    fi
    MSG_DL_CPU="正在安装 CPU PyTorch..."
    MSG_DONE="[TTS] 安装完成！正在启动服务器..."
    MSG_PRESS="按 Enter 键退出..."
else
    MSG_TITLE="[TTS] Select Action"
    MSG_OPT1="1 - Start Server (CPU / Default)"
    if [ "$OS_NAME" == "Darwin" ]; then
        MSG_OPT2="2 - Start Server (Apple Silicon MPS / Metal)"
    else
        MSG_OPT2="2 - Start Server (GPU NVIDIA)"
    fi
    MSG_OPT3="3 - Install/Repair Environment (Downloads Python)"
    MSG_PROMPT="Enter 1, 2, or 3 and press Enter: "
    MSG_INVALID="Invalid input, defaulting to 1."
    MSG_STARTING="[TTS] Starting Server..."
    MSG_INST_MODE="[TTS] Installation Mode"
    MSG_INST_CPU="1 - CPU Version (Standard)"
    if [ "$OS_NAME" == "Darwin" ]; then
        MSG_INST_GPU="2 - Apple Silicon Version (M1/M2/M3/M4 Metal Acceleration)"
    else
        MSG_INST_GPU="2 - GPU Version (Requires NVIDIA CUDA)"
    fi
    MSG_INST_PROMPT="Enter 1 or 2: "
    MSG_PATH_TITLE="[TTS] Installation Path Setup"
    MSG_PATH_1="Where do you want to install Python and dependencies?"
    MSG_PATH_2="Press ENTER to use the default path (inside the mod folder)."
    MSG_PATH_3="Example of custom path: $HOME/TTS_Env"
    MSG_PATH_PROMPT="Enter path or press ENTER: "
    MSG_INIT="[TTS] Initializing Environment..."
    MSG_INSTALL_UV="Installing uv package manager..."
    MSG_DL_PYTHON="Downloading portable Python 3.11 if not installed..."
    MSG_VENV="Creating isolated Python environment at "
    if [ "$OS_NAME" == "Darwin" ]; then
        MSG_DL_GPU="Installing PyTorch with Apple Silicon (MPS) support..."
    else
        MSG_DL_GPU="Installing GPU PyTorch (CUDA)..."
    fi
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
        if [ "$OS_NAME" == "Darwin" ]; then
            DEVICE_ARG="mps"
        else
            DEVICE_ARG="gpu"
        fi
    fi
    echo ""
    echo "=============================================="
    echo "$MSG_STARTING"
    echo "=============================================="
    PYTHON_EXE="python3"
    if [ -f "venv_path.txt" ]; then
        SAVED_VENV=$(cat venv_path.txt | tr -d '\r\n')
        if [ -f "$SAVED_VENV/bin/python" ]; then
            PYTHON_EXE="$SAVED_VENV/bin/python"
        elif [ -f "$SAVED_VENV/bin/python3" ]; then
            PYTHON_EXE="$SAVED_VENV/bin/python3"
        fi
    elif [ -f ".venv/bin/python" ]; then
        PYTHON_EXE=".venv/bin/python"
    elif [ -f ".venv/bin/python3" ]; then
        PYTHON_EXE=".venv/bin/python3"
    fi
    "$PYTHON_EXE" Server/silero_server.py "$LANG_ARG" "$DEVICE_ARG"
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
    export PATH="$HOME/.local/bin:$HOME/.cargo/bin:$PATH"
fi

echo "$MSG_DL_PYTHON"
uv python install 3.11

echo "$MSG_VENV$VENV_DIR..."
uv venv --python 3.11 "$VENV_DIR"

if [ "$OS_NAME" == "Darwin" ]; then
    echo "$MSG_DL_GPU"
    uv pip install --python "$VENV_DIR" torch torchaudio
elif [ "$inst_mode" == "2" ]; then
    echo "$MSG_DL_GPU"
    uv pip install --python "$VENV_DIR" torch torchaudio --index-url https://download.pytorch.org/whl/cu118 || uv pip install --python "$VENV_DIR" torch torchaudio
else
    echo "$MSG_DL_CPU"
    uv pip install --python "$VENV_DIR" torch torchaudio --index-url https://download.pytorch.org/whl/cpu || uv pip install --python "$VENV_DIR" torch torchaudio
fi

uv pip install --python "$VENV_DIR" soundfile flask omegaconf piper-tts requests g2pw unicode_rbnf sentence_stream

echo ""
echo "=============================================="
echo "$MSG_DONE"
echo "=============================================="
SETUP_DEVICE="cpu"
if [ "$inst_mode" == "2" ]; then
    if [ "$OS_NAME" == "Darwin" ]; then
        SETUP_DEVICE="mps"
    else
        SETUP_DEVICE="gpu"
    fi
fi

if [ -f "$VENV_DIR/bin/python" ]; then
    "$VENV_DIR/bin/python" Server/silero_server.py "$LANG_ARG" "$SETUP_DEVICE"
else
    "$VENV_DIR/bin/python3" Server/silero_server.py "$LANG_ARG" "$SETUP_DEVICE"
fi

read -p "$MSG_PRESS"
