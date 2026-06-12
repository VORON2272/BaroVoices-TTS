using System;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Concurrent;
using Barotrauma;
using MoonSharp.Interpreter;
using HarmonyLib;
using Microsoft.Xna.Framework;
using System.Reflection;
using System.Collections.Generic;

public class TTSModPlugin : IAssemblyPlugin
{
    public void Initialize()
    {
        try 
        {
            UserData.RegisterType(typeof(TTSManager));
            if (GameMain.LuaCs != null && GameMain.LuaCs.Lua != null)
            {
                GameMain.LuaCs.Lua.Globals["TTSManager"] = typeof(TTSManager);
            }
            TTSManager.Initialize();
        }
        catch (Exception e)
        {
            TTSManager.Log("[BaroVoices TTS] Error initializing C# plugin: " + e.Message);
        }
    }
    
    private Harmony harmony;

    public void OnLoadCompleted() {}
    public void PreInitPatching() 
    {
        try 
        {
            harmony = new Harmony("ttsmod.ui");
            harmony.Patch(
                original: typeof(GUI).GetMethod("TogglePauseMenu", BindingFlags.Public | BindingFlags.Static),
                postfix: new HarmonyMethod(typeof(TTSModMenu).GetMethod("OnTogglePauseMenu", BindingFlags.Public | BindingFlags.Static))
            );
        }
        catch (Exception e)
        {
            LuaCsLogger.LogError("[BaroVoices TTS] Failed to patch GUI.TogglePauseMenu: " + e.Message);
        }
    }
    
    public void Dispose() 
    {
        harmony?.UnpatchAll("ttsmod.ui");
        TTSModMenu.CloseMenu();
    }
}

public class TTSSettings
{
    public int GlobalVolume = 100;
    public int VolumeBoost = 100;
    public int BaseRate = 0;
    public bool EnableUniqueVoices = true;
    public int MyPitch = 0;
    public int MySpeed = 0;
    public string VoiceName = "";
    public bool DebugLogging = false;
    public bool EnableBotTTS = true;
    public int SampleRate = 24000;
}

public static class TTSManager
{
    public static TTSSettings Settings { get; private set; } = new TTSSettings();

    public static int GlobalVolume 
    { 
        get => Settings.GlobalVolume; 
        set { Settings.GlobalVolume = value; SaveSettings(); }
    }
    public static int VolumeBoost 
    { 
        get => Settings.VolumeBoost; 
        set { Settings.VolumeBoost = value; SaveSettings(); }
    }
    public static int BaseRate 
    { 
        get => Settings.BaseRate; 
        set { Settings.BaseRate = value; SaveSettings(); }
    }
    public static bool EnableUniqueVoices 
    { 
        get => Settings.EnableUniqueVoices; 
        set { Settings.EnableUniqueVoices = value; SaveSettings(); }
    }
    public static int MyPitch 
    { 
        get => Settings.MyPitch; 
        set { Settings.MyPitch = value; SaveSettings(); }
    }
    public static int MySpeed 
    { 
        get => Settings.MySpeed; 
        set { Settings.MySpeed = value; SaveSettings(); }
    }
    public static string VoiceName 
    { 
        get => Settings.VoiceName; 
        set { Settings.VoiceName = value; SaveSettings(); }
    }
    public static bool DebugLogging 
    { 
        get => Settings.DebugLogging; 
        set { Settings.DebugLogging = value; SaveSettings(); }
    }
    public static bool EnableBotTTS 
    { 
        get => Settings.EnableBotTTS; 
        set { Settings.EnableBotTTS = value; SaveSettings(); }
    }
    public static int SampleRate 
    { 
        get => Settings.SampleRate; 
        set { Settings.SampleRate = value; SaveSettings(); }
    }

    private static List<Tuple<Barotrauma.Sounds.SoundChannel, Character, string>> activeVoiceChannels = new List<Tuple<Barotrauma.Sounds.SoundChannel, Character, string>>();

    public static void Log(string message)
    {
        if (DebugLogging)
        {
            LuaCsLogger.Log(message);
        }
    }

    private static ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();

    public static void RunOnMainThread(Action action)
    {
        mainThreadActions.Enqueue(action);
    }

    public static void Update()
    {
        while (mainThreadActions.TryDequeue(out Action action))
        {
            try { action(); } 
            catch (Exception e) { TTSManager.Log("[BaroVoices TTS] MainThread Action Error: " + e.Message); }
        }

        checkServerTimer -= 1f / 60f;
        if (checkServerTimer <= 0f)
        {
            checkServerTimer = 1.0f;
            CheckServerStatusAsync();
        }

        if (StatusLabelRef != null && StatusLabelRef.RectTransform != null && StatusLabelRef.RectTransform.Parent != null)
        {
            StatusLabelRef.TextColor = IsServerRunning ? Color.LimeGreen : Color.Tomato;
            string prefix = IsRussianLanguage ? "Статус Сервера TTS: " : "TTS Server Status: ";
            StatusLabelRef.Text = prefix + ServerStatusText;
        }

        lock (activeVoiceChannels)
        {
            for (int i = activeVoiceChannels.Count - 1; i >= 0; i--)
            {
                var tuple = activeVoiceChannels[i];
                var channel = tuple.Item1;
                var character = tuple.Item2;

                var msgType = tuple.Item3;

                if (!channel.IsPlaying)
                {
                    activeVoiceChannels.RemoveAt(i);
                }
                else if (character != null && !character.Removed)
                {
                    channel.Position = new Vector3(character.WorldPosition.X, character.WorldPosition.Y, 0f);
                    
                    if (msgType != "Radio" && Character.Controlled != null)
                    {
                        bool isDifferentHull = character.CurrentHull != Character.Controlled.CurrentHull;
                        bool isMuffled = isDifferentHull;
                        
                        if (isDifferentHull && character.CurrentHull != null && Character.Controlled.CurrentHull != null)
                        {
                            try
                            {
                                foreach (var gap in character.CurrentHull.ConnectedGaps)
                                {
                                    if (gap.IsRoomToRoom && gap.Open > 0.1f)
                                    {
                                        foreach (var linked in gap.linkedTo)
                                        {
                                            if (linked == Character.Controlled.CurrentHull)
                                            {
                                                isMuffled = false;
                                                break;
                                            }
                                        }
                                    }
                                    if (!isMuffled) break;
                                }
                            }
                            catch { } // Fallback if reflection or property fails
                        }

                        channel.Muffled = isMuffled;
                    }
                }
            }
        }
    }

    public static List<string> GetAvailableVoices()
    {
        return new List<string> { "aidar", "baya", "kseniya", "xenia", "eugene", "en_0", "en_1", "en_2", "en_3", "en_4", "en_5" };
    }

    private static void SendTTSRequest(Character character, string text, string voice, int rate, int volume, string msgType, float distance)
    {
        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                string escapedText = text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
                string json = $"{{\"text\":\"{escapedText}\",\"voice\":\"{voice}\",\"rate\":{rate},\"volume\":{volume},\"boost\":{Settings.VolumeBoost},\"msg_type\":\"{msgType}\",\"distance\":{distance.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)},\"sample_rate\":{Settings.SampleRate}}}";
                var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(30);
                    var response = await client.PostAsync("http://127.0.0.1:5000/tts", content);
                    if (response.IsSuccessStatusCode)
                    {
                        byte[] wavBytes = await response.Content.ReadAsByteArrayAsync();
                        PlayWavBytes(character, wavBytes, volume, msgType, distance);
                    }
                    else
                    {
                        TTSManager.Log("[BaroVoices TTS] HTTP Error: " + response.StatusCode);
                    }
                }
            }
            catch (Exception e)
            {
                TTSManager.Log("[BaroVoices TTS] Network TTS Error: " + e.Message);
            }
        });
    }

    public static string ServerStatusText = "Checking...";
    public static bool IsServerRunning = false;
    public static GUITextBlock StatusLabelRef = null;
    public static bool IsRussianLanguage = false;
    private static float checkServerTimer = 0f;

    public static async void CheckServerStatusAsync()
    {
        try
        {
            using (var client = new System.Net.Http.HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(1);
                var response = await client.GetAsync("http://127.0.0.1:5000/voices");
                IsServerRunning = response.IsSuccessStatusCode;
                ServerStatusText = IsServerRunning ? (IsRussianLanguage ? "Работает (OK)" : "Running (OK)") : (IsRussianLanguage ? "Не запущен" : "Not running");
            }
        }
        catch
        {
            IsServerRunning = false;
            ServerStatusText = IsRussianLanguage ? "Не запущен" : "Not running";
        }
    }

    private static void PlayWavBytes(Character character, byte[] wavBytes, int volume, string msgType, float distance)
    {
        try
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "BarotraumaTTS");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
            
            string tempFile = Path.Combine(tempDir, "tts_" + Guid.NewGuid().ToString() + ".ogg");
            File.WriteAllBytes(tempFile, wavBytes);

            int safeVolume = Math.Max(10, volume);
            TTSManager.Log($"[BaroVoices TTS] PlayWavBytes: Saved temp file {tempFile}. Safe Volume: {safeVolume}");

            var sound = GameMain.SoundManager.LoadSound(tempFile, false);
            if (sound != null)
            {
                TTSManager.Log("[BaroVoices TTS] Sound loaded. Waiting 200ms before Play()...");
                Task.Delay(200).ContinueWith(_ => {
                    try
                    {
                        float gain = (safeVolume / 100f) * 2.5f;
                        Vector2 pos = character != null ? character.WorldPosition : Vector2.Zero;
                        var channel = sound.Play(gain, 1500f, pos);
                        if (channel != null) {
                            if (msgType != "Radio") channel.Muffled = false; // Muffle will be calculated dynamically in Update()
                        }

                        if (channel == null)
                        {
                            TTSManager.Log("[BaroVoices TTS] WARNING: sound.Play() returned null! Trying again in 300ms...");
                            Task.Delay(300).ContinueWith(__ => {
                                float retryGain = (safeVolume / 100f) * 2.5f;
                                Vector2 retryPos = character != null ? character.WorldPosition : Vector2.Zero;
                                var retryChannel = sound.Play(retryGain, 1500f, retryPos);
                                if (retryChannel != null) {
                                    if (msgType != "Radio") {
                                        retryChannel.Muffled = false; // Updated dynamically
                                        if (character != null) {
                                            retryChannel.Position = new Vector3(character.WorldPosition.X, character.WorldPosition.Y, 0f);
                                            lock (activeVoiceChannels) { activeVoiceChannels.Add(Tuple.Create(retryChannel, character, msgType)); }
                                        }
                                    }
                                }
                            });
                        }
                        else
                        {
                            TTSManager.Log("[BaroVoices TTS] SUCCESS: Audio channel started playing.");
                            if (msgType != "Radio" && character != null)
                            {
                                channel.Position = new Vector3(character.WorldPosition.X, character.WorldPosition.Y, 0f);
                                lock (activeVoiceChannels) { activeVoiceChannels.Add(Tuple.Create(channel, character, msgType)); }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        TTSManager.Log("[BaroVoices TTS] Delayed Play Error: " + ex.Message);
                    }
                });
            }
            else
            {
                TTSManager.Log("[BaroVoices TTS] ERROR: LoadSound returned null.");
            }

            Task.Delay(10000).ContinueWith(_ => {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            });
        }
        catch (Exception ex)
        {
            TTSManager.Log("[BaroVoices TTS] PlayWavBytes Error: " + ex.Message);
        }
    }

    public static void Speak(Character character, string text, string msgType = "Default")
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (character != null && character.IsBot && !Settings.EnableBotTTS) return;
        string charName = character != null ? character.Name : "Server";

        string voice = Settings.VoiceName;
        if (string.IsNullOrEmpty(voice)) voice = "baya";
        
        if (character != null && character == Character.Controlled)
        {
            voice = Settings.VoiceName;
            if (string.IsNullOrEmpty(voice)) voice = "baya";
        }
        else if (Settings.EnableUniqueVoices)
        {
            int hash = Math.Abs(charName.GetHashCode());
            if (character != null && character.Info != null)
            {
                string genderStr = "";
                try 
                {
                    var infoType = character.Info.GetType();
                    var gField = infoType.GetField("Gender", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (gField != null) genderStr = gField.GetValue(character.Info)?.ToString();
                    else 
                    {
                        var gProp = infoType.GetProperty("Gender", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (gProp != null) genderStr = gProp.GetValue(character.Info)?.ToString();
                        else 
                        {
                            var pProp = infoType.GetProperty("Pronouns", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (pProp != null) genderStr = pProp.GetValue(character.Info)?.ToString();
                        }
                    }
                } 
                catch { }

                if (genderStr == "Male" || genderStr == "He")
                {
                    string[] maleVoices = { "aidar", "eugene" };
                    voice = maleVoices[hash % maleVoices.Length];
                }
                else if (genderStr == "Female" || genderStr == "She")
                {
                    string[] femaleVoices = { "baya", "kseniya", "xenia" };
                    voice = femaleVoices[hash % femaleVoices.Length];
                }
                else
                {
                    var voices = GetAvailableVoices();
                    voice = voices[hash % voices.Count];
                }
            }
            else
            {
                var voices = GetAvailableVoices();
                voice = voices[hash % voices.Count];
            }
        }
        
        float distance = 0f;
        Vector2 listenerPos = Character.Controlled != null ? Character.Controlled.WorldPosition : (GameMain.GameScreen.Cam != null ? GameMain.GameScreen.Cam.WorldViewCenter : Vector2.Zero);
        if (character != null && listenerPos != Vector2.Zero)
        {
            distance = Vector2.Distance(listenerPos, character.WorldPosition);
        }

        if (msgType != "Radio" && distance > 4000f)
        {
            TTSManager.Log($"[BaroVoices TTS] Skipped generation for {charName}. Too far away ({distance}).");
            return;
        }

        SendTTSRequest(character, text, voice, Settings.BaseRate, Settings.GlobalVolume, msgType, distance);
    }

    public static void SpeakWithCustom(Character character, string text, string customVoice, int customRate, string msgType = "Default")
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (character != null && character.IsBot && !Settings.EnableBotTTS) return;
        
        string voice = string.IsNullOrEmpty(customVoice) ? "baya" : customVoice;
        
        float distance = 0f;
        Vector2 listenerPos = Character.Controlled != null ? Character.Controlled.WorldPosition : (GameMain.GameScreen.Cam != null ? GameMain.GameScreen.Cam.WorldViewCenter : Vector2.Zero);
        if (character != null && listenerPos != Vector2.Zero)
        {
            distance = Vector2.Distance(listenerPos, character.WorldPosition);
        }

        if (msgType != "Radio" && distance > 4000f)
        {
            TTSManager.Log($"[BaroVoices TTS] Skipped custom generation for {character?.Name}. Too far away ({distance}).");
            return;
        }

        SendTTSRequest(character, text, voice, Settings.BaseRate + customRate, Settings.GlobalVolume, msgType, distance);
    }

    public static void Initialize()
    {
        LoadSettings();
    }

    public static string GetScriptPath(string scriptName)
    {
        List<string> rootDirs = new List<string> 
        { 
            "LocalMods", 
            "WorkshopMods/Installed",
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Daedalic Entertainment GmbH", "Barotrauma", "WorkshopMods", "Installed")
        };

        foreach (var root in rootDirs)
        {
            if (System.IO.Directory.Exists(root))
            {
                foreach (var dir in System.IO.Directory.GetDirectories(root))
                {
                    string p = System.IO.Path.Combine(dir, scriptName);
                    if (System.IO.File.Exists(p) && System.IO.File.Exists(System.IO.Path.Combine(dir, "Server", "silero_server.py"))) 
                        return p;
                }
            }
        }
        return "LocalMods/BaroVoices TTS/" + scriptName;
    }

    private static void LoadSettings()
    {
        try
        {
            Settings = new TTSSettings();
            string path = "TTSModSettings.txt";
            if (System.IO.File.Exists(path))
            {
                string[] lines = System.IO.File.ReadAllLines(path);
                foreach(string line in lines)
                {
                    string[] parts = line.Split('=');
                    if (parts.Length == 2)
                    {
                        string k = parts[0];
                        string v = parts[1];
                        if (k == "GlobalVolume" && int.TryParse(v, out int gv)) Settings.GlobalVolume = gv;
                        if (k == "VolumeBoost" && int.TryParse(v, out int vb)) Settings.VolumeBoost = vb;
                        if (k == "BaseRate" && int.TryParse(v, out int br)) Settings.BaseRate = br;
                        if (k == "EnableUniqueVoices" && bool.TryParse(v, out bool eu)) Settings.EnableUniqueVoices = eu;
                        if (k == "MyPitch" && int.TryParse(v, out int mp)) Settings.MyPitch = mp;
                        if (k == "MySpeed" && int.TryParse(v, out int ms)) Settings.MySpeed = ms;
                        if (k == "VoiceName") Settings.VoiceName = v;
                        if (k == "DebugLogging" && bool.TryParse(v, out bool dl)) Settings.DebugLogging = dl;
                        if (k == "EnableBotTTS" && bool.TryParse(v, out bool ebt)) Settings.EnableBotTTS = ebt;
                        if (k == "SampleRate" && int.TryParse(v, out int sr)) Settings.SampleRate = sr;
                    }
                }
            }
            else
            {
                SaveSettings();
            }
        }
        catch (Exception ex)
        {
            TTSManager.Log("[BaroVoices TTS] LoadSettings Error: " + ex.Message);
        }
    }

    public static void SaveSettings()
    {
        try
        {
            string[] lines = {
                "GlobalVolume=" + Settings.GlobalVolume,
                "VolumeBoost=" + Settings.VolumeBoost,
                "BaseRate=" + Settings.BaseRate,
                "EnableUniqueVoices=" + Settings.EnableUniqueVoices,
                "MyPitch=" + Settings.MyPitch,
                "MySpeed=" + Settings.MySpeed,
                "VoiceName=" + Settings.VoiceName,
                "DebugLogging=" + Settings.DebugLogging,
                "EnableBotTTS=" + Settings.EnableBotTTS,
                "SampleRate=" + Settings.SampleRate
            };
            System.IO.File.WriteAllLines("TTSModSettings.txt", lines);
        }
        catch (Exception ex)
        {
            LuaCsLogger.LogError("[BaroVoices TTS] Failed to save settings: " + ex.Message);
        }
    }
}

public static class TTSModMenu
{
    private static GUIFrame currentFrame;

    private static List<GUIComponent> GetChildren(GUIComponent component)
    {
        List<GUIComponent> list = new List<GUIComponent>();
        if (component == null) return list;
        foreach (GUIComponent child in component.Children)
        {
            list.Add(child);
        }
        return list;
    }

    public static void OnTogglePauseMenu()
    {
        try
        {
            if (GUI.PauseMenuOpen)
            {
                GUIFrame pauseMenu = GUI.PauseMenu;
                if (pauseMenu == null) return;

                List<GUIComponent> children = GetChildren(pauseMenu);
                if (children.Count > 1)
                {
                    List<GUIComponent> children2 = GetChildren(children[1]);
                    if (children2.Count > 0)
                    {
                        for (int i = 0; i < children2.Count; i++)
                        {
                            if (children2[i] is GUIButton existingBtn && existingBtn.Text == "BaroVoices TTS")
                            {
                                return;
                            }
                        }

                        GUIButton btn = new GUIButton(new RectTransform(new Vector2(1f, 0.1f), children2[0].RectTransform, Anchor.TopLeft, Pivot.TopLeft, null), "BaroVoices TTS", Alignment.Center, "GUIButton", null);
                        btn.OnClicked = (b, userdata) =>
                        {
                            try
                            {
                                ToggleMenu();
                            }
                            catch (Exception ex)
                            {
                                LuaCsLogger.LogError("[BaroVoices TTS] Click error: " + ex.Message);
                            }
                            return true;
                        };
                    }
                }
            }
            else
            {
                CloseMenu();
            }
        }
        catch (Exception e)
        {
            LuaCsLogger.LogError("[BaroVoices TTS] Error in OnTogglePauseMenu: " + e.Message);
        }
    }

    public static void CloseMenu()
    {
        try 
        {
            if (currentFrame != null)
            {
                TTSManager.Log("[BaroVoices TTS] CloseMenu: Removing currentFrame");
                if (currentFrame.RectTransform != null)
                {
                    currentFrame.RectTransform.Parent = null;
                }
            }
        }
        catch { }
        finally 
        {
            currentFrame = null;
        }
    }

    public static void ToggleMenu()
    {
        try
        {
            if (currentFrame != null)
            {
                TTSManager.Log("[BaroVoices TTS] ToggleMenu: Frame exists, closing it.");
                CloseMenu();
                return;
            }

            TTSManager.Log("[BaroVoices TTS] ToggleMenu: Creating new GUIFrame on GUI.PauseMenu...");
            currentFrame = new GUIFrame(new RectTransform(new Vector2(0.55f, 0.65f), GUI.PauseMenu.RectTransform, Anchor.Center), "GUIFrame");
            currentFrame.CanBeFocused = true;
            
            var mainHorizontal = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), currentFrame.RectTransform, Anchor.Center), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            bool isRussian = false;
            try
            {
                var settingsProp = typeof(GameMain).Assembly.GetType("Barotrauma.GameSettings")?.GetProperty("CurrentConfig");
                if (settingsProp != null)
                {
                    var config = settingsProp.GetValue(null);
                    var lang = config?.GetType().GetProperty("Language")?.GetValue(config) ?? config?.GetType().GetField("Language")?.GetValue(config);
                    if (lang != null && lang.ToString().ToLower().Contains("ru")) isRussian = true;
                }
            }
            catch { }
            TTSManager.IsRussianLanguage = isRussian;

            string tabGameplay = isRussian ? "Геймплей" : "Gameplay";
            string tabServer = isRussian ? "Оптимизация" : "Server & Perf";
            string tabPersonal = isRussian ? "Мой голос" : "My Voice";
            string titleText = isRussian ? "Настройки TTS" : "BaroVoices TTS";
            string closeText = isRussian ? "Закрыть" : "Close";

            var tabBar = new GUILayoutGroup(new RectTransform(new Vector2(0.3f, 1f), mainHorizontal.RectTransform))
            {
                Stretch = false,
                RelativeSpacing = 0.05f
            };

            var contentArea = new GUIFrame(new RectTransform(new Vector2(0.7f, 1f), mainHorizontal.RectTransform), style: null);

            TTSManager.CheckServerStatusAsync();

            Action createGameplayTab = () => 
            {
                contentArea.ClearChildren();
                var layout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), contentArea.RectTransform, Anchor.Center)) { RelativeSpacing = 0.02f };

                new GUITextBlock(new RectTransform(new Vector2(1f, 0.06f), layout.RectTransform), tabGameplay, textAlignment: Alignment.Center);
                
                string hint1 = isRussian ? "Настройки геймплея. Действуют на всех игроков." : "Gameplay settings. Apply to all players.";
                new GUITextBlock(new RectTransform(new Vector2(1f, 0.10f), layout.RectTransform), hint1, textAlignment: Alignment.TopCenter, wrap: true) { TextColor = Color.LightCyan };

                var audioBlock = new GUIFrame(new RectTransform(new Vector2(1f, 0.70f), layout.RectTransform), style: "InnerFrame");
                var audioLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), audioBlock.RectTransform, Anchor.Center)) { RelativeSpacing = 0.03f };

                var volContainer = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.15f), audioLayout.RectTransform), isHorizontal: true) { RelativeSpacing = 0.05f };
                string volTxt = isRussian ? "Общая Громкость: " : "Global Volume: ";
                var volLabel = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), volContainer.RectTransform), volTxt + TTSManager.GlobalVolume + "%", textAlignment: Alignment.CenterLeft);
                var volumeScroll = new GUIScrollBar(new RectTransform(new Vector2(0.45f, 1f), volContainer.RectTransform), barSize: 0.1f, style: "GUISlider")
                {
                    BarScroll = Math.Max(0f, Math.Min(1f, TTSManager.GlobalVolume / 100f))
                };
                volumeScroll.OnMoved = (scrollbar, value) => 
                { 
                    TTSManager.GlobalVolume = (int)(value * 100f); 
                    volLabel.Text = volTxt + TTSManager.GlobalVolume + "%";
                    return true; 
                };
                volumeScroll.ToolTip = isRussian ? "Общая громкость мода. 100% = нормальная громкость в игре." : "Global mod volume. 100% = normal in-game volume.";

                var boostContainer = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.15f), audioLayout.RectTransform), isHorizontal: true) { RelativeSpacing = 0.05f };
                string boostTxt = isRussian ? "Усиление (Boost): " : "Volume Boost: ";
                var boostLabel = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), boostContainer.RectTransform), boostTxt + TTSManager.VolumeBoost + "%", textAlignment: Alignment.CenterLeft);
                var boostScroll = new GUIScrollBar(new RectTransform(new Vector2(0.45f, 1f), boostContainer.RectTransform), barSize: 0.1f, style: "GUISlider")
                {
                    BarScroll = Math.Max(0f, Math.Min(1f, (TTSManager.VolumeBoost - 100f) / 400f))
                };
                boostScroll.OnMoved = (scrollbar, value) => 
                { 
                    TTSManager.VolumeBoost = 100 + (int)(value * 400f); 
                    boostLabel.Text = boostTxt + TTSManager.VolumeBoost + "%";
                    return true; 
                };
                boostScroll.ToolTip = isRussian ? "Усиление звука до 500% (полезно, если голоса кажутся слишком тихими)." : "Boosts audio up to 500% (useful if voices are too quiet).";

                var speedContainer = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.15f), audioLayout.RectTransform), isHorizontal: true) { RelativeSpacing = 0.05f };
                string spdTxt = isRussian ? "Базовая скорость: " : "Base Speed: ";
                var speedLabel = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), speedContainer.RectTransform), spdTxt + TTSManager.BaseRate, textAlignment: Alignment.CenterLeft);
                var speedScroll = new GUIScrollBar(new RectTransform(new Vector2(0.45f, 1f), speedContainer.RectTransform), barSize: 0.1f, style: "GUISlider")
                {
                    BarScroll = Math.Max(0f, Math.Min(1f, (TTSManager.BaseRate + 10f) / 20f))
                };
                speedScroll.OnMoved = (scrollbar, value) => 
                { 
                    TTSManager.BaseRate = (int)((value * 20f) - 10f); 
                    speedLabel.Text = spdTxt + TTSManager.BaseRate;
                    return true; 
                };
                speedScroll.ToolTip = isRussian ? "Скорость чтения для всех персонажей. 0 = нормальная." : "Base reading speed for all characters. 0 = normal.";

                var checksRow1 = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.15f), audioLayout.RectTransform), isHorizontal: true) { RelativeSpacing = 0.05f };

                string uniqTxt = isRussian ? "Авто-выбор голосов" : "Auto-assign voices";
                var uniqueBox = new GUITickBox(new RectTransform(new Vector2(0.45f, 1f), checksRow1.RectTransform), uniqTxt)
                {
                    Selected = TTSManager.EnableUniqueVoices,
                    ToolTip = isRussian ? "Боты и другие игроки получат случайный подходящий им голос." : "Assigns a random fitting voice to bots/players."
                };
                uniqueBox.OnSelected = (tickBox) => 
                { 
                    TTSManager.EnableUniqueVoices = tickBox.Selected; 
                    return true; 
                };

                var botBox = new GUITickBox(new RectTransform(new Vector2(0.45f, 1f), checksRow1.RectTransform), isRussian ? "Озвучка Ботов" : "Enable Bot TTS")
                {
                    Selected = TTSManager.EnableBotTTS,
                    ToolTip = isRussian ? "Нужно ли озвучивать фразы ИИ ботов (экипажа, бандитов)." : "Should AI bot dialogues be generated and played."
                };
                botBox.OnSelected = (tickBox) => 
                { 
                    TTSManager.EnableBotTTS = tickBox.Selected; 
                    return true; 
                };
            };

            Action createServerTab = () => 
            {
                contentArea.ClearChildren();
                var layout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), contentArea.RectTransform, Anchor.Center)) { RelativeSpacing = 0.02f };

                new GUITextBlock(new RectTransform(new Vector2(1f, 0.06f), layout.RectTransform), tabServer, textAlignment: Alignment.Center);
                
                string hint1 = isRussian ? "Сервер, производительность и качество голоса." : "Server, performance and quality settings.";
                new GUITextBlock(new RectTransform(new Vector2(1f, 0.10f), layout.RectTransform), hint1, textAlignment: Alignment.TopCenter, wrap: true) { TextColor = Color.LightCyan };

                var serverBlock = new GUIFrame(new RectTransform(new Vector2(1f, 0.28f), layout.RectTransform), style: "InnerFrame");
                var serverLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), serverBlock.RectTransform, Anchor.Center), isHorizontal: false) { RelativeSpacing = 0.05f };
                string statusPrefix = isRussian ? "Статус Сервера TTS: " : "TTS Server Status: ";
                var statusLabel = new GUITextBlock(new RectTransform(new Vector2(1f, 0.4f), serverLayout.RectTransform), statusPrefix + TTSManager.ServerStatusText, textAlignment: Alignment.Center);
                statusLabel.TextColor = TTSManager.IsServerRunning ? Color.LimeGreen : Color.Tomato;
                TTSManager.StatusLabelRef = statusLabel;

                string startBtnTxt = isRussian ? "► ЗАПУСТИТЬ СЕРВЕР" : "► START SERVER";
                var startBtn = new GUIButton(new RectTransform(new Vector2(0.7f, 0.45f), serverLayout.RectTransform, Anchor.TopCenter), startBtnTxt, Alignment.Center, "GUIButton");
                startBtn.TextColor = Color.LightGreen;
                startBtn.OnClicked = (btn, ud) =>
                {
                    try
                    {
                        bool isLinux = System.Environment.OSVersion.Platform == PlatformID.Unix;
                        string scriptName = isLinux ? "start_server.sh" : "start_server.bat";
                        string modPath = TTSManager.GetScriptPath(scriptName);
                        if (System.IO.File.Exists(modPath))
                        {
                            string langArg = TTSManager.IsRussianLanguage ? "ru" : "en";
                            if (isLinux)
                            {
                                System.Diagnostics.Process.Start(new ProcessStartInfo
                                {
                                    FileName = "/bin/bash",
                                    Arguments = $"-c \"x-terminal-emulator -e \\\"bash '{System.IO.Path.GetFullPath(modPath)}' {langArg}\\\" || gnome-terminal -- bash '{System.IO.Path.GetFullPath(modPath)}' {langArg} || xterm -e bash '{System.IO.Path.GetFullPath(modPath)}' {langArg}\"",
                                    UseShellExecute = false
                                });
                            }
                            else
                            {
                                System.Diagnostics.Process.Start("cmd.exe", $"/c start \"\" \"{System.IO.Path.GetFullPath(modPath)}\" {langArg}");
                            }
                            startBtn.Text = isRussian ? "Запускается..." : "Starting...";
                        }
                    }
                    catch (Exception ex)
                    {
                        TTSManager.Log("[BaroVoices TTS] Failed to start server: " + ex.Message);
                    }
                    return true;
                };

                var perfBlock = new GUIFrame(new RectTransform(new Vector2(1f, 0.46f), layout.RectTransform), style: "InnerFrame");
                var perfLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.9f), perfBlock.RectTransform, Anchor.Center)) { RelativeSpacing = 0.05f };

                var qualRow = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.3f), perfLayout.RectTransform), isHorizontal: true) { RelativeSpacing = 0.05f };
                string qualTxt = isRussian ? "Качество голоса:" : "Voice Quality:";
                new GUITextBlock(new RectTransform(new Vector2(0.45f, 1f), qualRow.RectTransform), qualTxt, textAlignment: Alignment.CenterLeft);
                var qualDrop = new GUIDropDown(new RectTransform(new Vector2(0.5f, 1f), qualRow.RectTransform), "Quality", 3);
                qualDrop.AddItem(isRussian ? "Высокое (48000 Hz)" : "High (48000 Hz)", 48000);
                qualDrop.AddItem(isRussian ? "Баланс (24000 Hz)" : "Balanced (24000 Hz)", 24000);
                qualDrop.AddItem(isRussian ? "Рация (8000 Hz)" : "Radio (8000 Hz)", 8000);
                
                qualDrop.SelectItem(TTSManager.SampleRate);
                qualDrop.OnSelected = (c, o) => { 
                    if (o is int sr) TTSManager.SampleRate = sr; 
                    return true; 
                };
                qualDrop.ToolTip = isRussian ? "Влияет на чистоту звука. 48000 Hz требует больше ресурсов процессора, но голос менее 'роботизированный'." : "Affects clarity. 48000 Hz uses more CPU but sounds less robotic.";

                var checksRow2 = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.3f), perfLayout.RectTransform), isHorizontal: true) { RelativeSpacing = 0.05f };
                var debugBox = new GUITickBox(new RectTransform(new Vector2(0.45f, 1f), checksRow2.RectTransform), "Debug Logging")
                {
                    Selected = TTSManager.DebugLogging,
                    ToolTip = isRussian ? "Показывать системную информацию мода в консоли игры (F3)." : "Show technical mod logs in the game console (F3)."
                };
                debugBox.OnSelected = (tickBox) => 
                { 
                    TTSManager.DebugLogging = tickBox.Selected; 
                    return true; 
                };
            };

            Action createPersonalTab = () => 
            {
                contentArea.ClearChildren();
                var layout = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.95f), contentArea.RectTransform, Anchor.Center)) { RelativeSpacing = 0.05f };

                new GUITextBlock(new RectTransform(new Vector2(1f, 0.08f), layout.RectTransform), tabPersonal, textAlignment: Alignment.Center);
                
                string hint2 = isRussian ? "Настрой голос СВОЕГО персонажа!\nОбязательно нажми 'Применить и Отправить', чтобы другие игроки на сервере услышали изменения." : "Customize YOUR character's voice!\nBe sure to click 'Apply & Sync' to share it with other players.";
                new GUITextBlock(new RectTransform(new Vector2(1f, 0.15f), layout.RectTransform), hint2, textAlignment: Alignment.TopCenter, wrap: true) { TextColor = Color.LightYellow };

                var voiceBlock = new GUIFrame(new RectTransform(new Vector2(1f, 0.5f), layout.RectTransform), style: "InnerFrame");
                var voiceLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.9f, 0.9f), voiceBlock.RectTransform, Anchor.Center)) { RelativeSpacing = 0.05f };

                var voiceContainer = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.2f), voiceLayout.RectTransform), isHorizontal: true) { RelativeSpacing = 0.05f };
                new GUITextBlock(new RectTransform(new Vector2(0.45f, 1f), voiceContainer.RectTransform), isRussian ? "Мой голос (Модель): " : "My Voice Model: ", textAlignment: Alignment.CenterLeft);
                var voiceDropdown = new GUIDropDown(new RectTransform(new Vector2(0.5f, 1f), voiceContainer.RectTransform), "Select Voice", 5);
                var voices = TTSManager.GetAvailableVoices();
                foreach (var v in voices) { voiceDropdown.AddItem(v, v); }
                if (!string.IsNullOrEmpty(TTSManager.VoiceName) && voices.Contains(TTSManager.VoiceName))
                    voiceDropdown.SelectItem(TTSManager.VoiceName);
                else if (voices.Count > 0)
                    voiceDropdown.SelectItem(voices[0]);
                    
                voiceDropdown.OnSelected = (component, obj) => {
                    TTSManager.VoiceName = obj as string;
                    return true;
                };
                voiceDropdown.ToolTip = isRussian ? "Выберите модель голоса, которой будет говорить ваш персонаж в игре." : "Choose the voice model your character will use in-game.";

                var speedContainer = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.2f), voiceLayout.RectTransform), isHorizontal: true) { RelativeSpacing = 0.05f };
                string spdTxt = isRussian ? "Моя скорость речи: " : "My Speech Speed: ";
                var speedLabel = new GUITextBlock(new RectTransform(new Vector2(0.5f, 1f), speedContainer.RectTransform), spdTxt + TTSManager.MySpeed, textAlignment: Alignment.CenterLeft);
                var speedScroll = new GUIScrollBar(new RectTransform(new Vector2(0.45f, 1f), speedContainer.RectTransform), barSize: 0.1f, style: "GUISlider")
                {
                    BarScroll = Math.Max(0f, Math.Min(1f, (TTSManager.MySpeed + 10f) / 20f))
                };
                speedScroll.OnMoved = (scrollbar, value) => 
                { 
                    TTSManager.MySpeed = (int)((value * 20f) - 10f); 
                    speedLabel.Text = spdTxt + TTSManager.MySpeed;
                    return true; 
                };
                speedScroll.ToolTip = isRussian ? "Индивидуальная скорость вашей речи (прибавляется к базовой)." : "Your personal speaking speed modifier.";

                new GUITextBlock(new RectTransform(new Vector2(1f, 0.05f), voiceLayout.RectTransform), "");

                var btnLayout = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.25f), voiceLayout.RectTransform), isHorizontal: true) { RelativeSpacing = 0.05f };
                
                string prevTxt = isRussian ? "Прослушать" : "Preview";
                var previewBtn = new GUIButton(new RectTransform(new Vector2(0.45f, 1f), btnLayout.RectTransform), prevTxt, Alignment.Center, "GUIButton");
                previewBtn.OnClicked = (btn, ud) =>
                {
                    string textToSpeak = isRussian ? "Внимание экипажу! Проверка системы связи, как слышно?" : "Attention crew! Radio comms test, how do you copy?";
                    string myVoice = TTSManager.VoiceName;
                    if (string.IsNullOrEmpty(myVoice)) myVoice = "baya";
                    
                    if (Character.Controlled != null)
                        TTSManager.SpeakWithCustom(Character.Controlled, textToSpeak, myVoice, TTSManager.MySpeed);
                    else
                        TTSManager.SpeakWithCustom(null, textToSpeak, myVoice, TTSManager.MySpeed);
                    return true;
                };

                string syncTxt = isRussian ? "Применить и Отправить" : "Apply & Sync";
                var syncBtn = new GUIButton(new RectTransform(new Vector2(0.5f, 1f), btnLayout.RectTransform), syncTxt, Alignment.Center, "GUIButton");
                syncBtn.TextColor = Color.LightGreen;
                syncBtn.OnClicked = (btn, ud) =>
                {
                    try
                    {
                        if (Character.Controlled == null)
                        {
                            syncBtn.Text = isRussian ? "Только в игре!" : "In-game only!";
                            return true;
                        }

                        string myVoice = TTSManager.VoiceName;
                        if (string.IsNullOrEmpty(myVoice)) myVoice = "baya";
                        string luaCmd = $"if SendMyVoiceSettings then SendMyVoiceSettings({TTSManager.MyPitch}, {TTSManager.MySpeed}, \"{myVoice}\") end";
                        GameMain.LuaCs.Lua.DoString(luaCmd);
                        syncBtn.Text = isRussian ? "Успешно отправлено!" : "Synced!";
                    }
                    catch (Exception ex)
                    {
                        TTSManager.Log("[BaroVoices TTS] Sync failed: " + ex.Message);
                    }
                    return true;
                };
            };

            var btnGameplay = new GUIButton(new RectTransform(new Vector2(1f, 0.15f), tabBar.RectTransform), tabGameplay, Alignment.Center, "GUIButton");
            btnGameplay.OnClicked = (btn, ud) => { createGameplayTab(); return true; };

            var btnServer = new GUIButton(new RectTransform(new Vector2(1f, 0.15f), tabBar.RectTransform), tabServer, Alignment.Center, "GUIButton");
            btnServer.OnClicked = (btn, ud) => { createServerTab(); return true; };

            var btnPersonal = new GUIButton(new RectTransform(new Vector2(1f, 0.15f), tabBar.RectTransform), tabPersonal, Alignment.Center, "GUIButton");
            btnPersonal.OnClicked = (btn, ud) => { createPersonalTab(); return true; };

            new GUITextBlock(new RectTransform(new Vector2(1f, 0.10f), tabBar.RectTransform), ""); // Filler

            var supportBtn = new GUIButton(new RectTransform(new Vector2(1f, 0.15f), tabBar.RectTransform), isRussian ? "Поддержать Автора" : "Support Author", Alignment.Center, "GUIButton");
            supportBtn.TextColor = Color.Gold;
            supportBtn.OnClicked = (btn, ud) =>
            {
                try {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "https://boosty.to/voron227",
                        UseShellExecute = true
                    });
                } catch { }
                return true;
            };

            var closeBtn = new GUIButton(new RectTransform(new Vector2(1f, 0.15f), tabBar.RectTransform), closeText, Alignment.Center, "GUIButton");
            closeBtn.OnClicked = (btn, ud) =>
            {
                CloseMenu();
                return true;
            };

            createGameplayTab();

            TTSManager.Log("[BaroVoices TTS] ToggleMenu: Successfully created all UI elements!");
        }
        catch (Exception ex)
        {
            LuaCsLogger.LogError("[BaroVoices TTS] ToggleMenu Error: " + ex.ToString());
            CloseMenu();
        }
    }
}
