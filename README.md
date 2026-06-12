<div align="center">
  <!-- ЗДЕСЬ ЗАМЕНИ ССЫЛКУ НА СВОЙ ЛОГОТИП (например загрузи картинку на GitHub в репозиторий и вставь ссылку) -->
  <img src="https://via.placeholder.com/800x300/1a1a2e/00d2ff?text=BaroVoices+TTS+Logo alt="BaroVoices TTS Logo" />
  <h1>BaroVoices TTS</h1>
  <p><b>Real-time AI Text-to-Speech (TTS) for Barotrauma Multiplayer</b></p>
</div>

## 📖 About
**BaroVoices TTS** is a powerful client-server mod that adds fully synchronized, high-quality neural network voice acting to Barotrauma's text chat. Built on the ultra-fast **Silero TTS** models, it runs locally on your machine without requiring external Discord bots or paid APIs.

When you type in the text chat, your character physically speaks it out loud. 

## ✨ Features
* **Custom Voices:** Choose from multiple unique voice models (male and female) directly from the in-game UI.
* **Speed & Pitch Control:** Adjust your character's speaking speed for a truly unique sound. All changes instantly sync across the network to other players.
* **Multi-language Support:** Automatically detects and perfectly reads both **English** and **Russian** text.
* **Immersive Audio Filters:** Audio is processed through Barotrauma's sound engine! Radio messages have static and frequency filters applied, while local chat is muffled through submarine walls and fades with distance.
* **AI Crew Voices:** Bot crew members are assigned random voices and will speak their autonomous chat lines out loud.
* **High Performance:** Runs a local Python backend. Supports CPU multithreading and Nvidia GPU (CUDA) acceleration for zero-latency voice generation.
* **Cross-platform:** Fully supports Windows and Linux (including Dedicated Servers and Steam Deck).

## ⚙️ Installation

> **⚠️ IMPORTANT:** This mod requires an external Python backend to generate the neural network voices. Simply subscribing to the mod is **not enough**.

### Setup Instructions
1. Download the mod via Steam Workshop or clone this repository into your `Barotrauma/LocalMods` folder.
2. Enable the mod in the main menu alongside **Lua For Barotrauma** (Client-side Lua must be installed and active).
3. Open the **BaroVoices TTS** menu in the game (via the dedicated UI button).
4. Go to the **Server** tab and click **Start Server**.
5. A console window will open. If this is your first time, follow the on-screen prompts (Press 3 to install). It will automatically download Python and the required voice models.
6. Once the console says `Models loaded successfully!`, you are ready to play!

*(Note: If you are playing on someone else's server that already has the backend running, you do not need to start the backend server yourself!)*

## 🎮 Usage
- Open the **Personal** tab in the Mod UI to select your preferred English or Russian Voice Model.
- Adjust your Speech Speed and click **Apply & Sync**.
- Simply type into the Local or Radio chat to hear your character speak!

## 🛠️ Tech Stack
* **LuaForBarotrauma:** Networking, synchronization, and game-engine hooks.
* **C# (Barotrauma API):** User Interface, settings management, and FMOD audio playback.
* **Python (Silero TTS & PyTorch):** Local lightweight HTTP server generating audio tensors on the fly.

## 🤝 Contributing
Pull requests are welcome! If you find a bug or have a feature request, please open an issue.
