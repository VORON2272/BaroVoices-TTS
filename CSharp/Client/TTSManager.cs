using System;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Collections.Concurrent;
using Barotrauma;
using Barotrauma.Networking;
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
    public string TTSEngine = "silero";
    public bool DebugLogging = false;
    public bool EnableBotTTS = true;
    public int SampleRate = 24000;
    public bool EnableVoiceQueue = true;
    public bool EnableSuitMuffle = true;
    public bool EnableRadioFilter = true;
}

public class QueuedVoiceAudio
{
    public Character Character;
    public byte[] WavBytes;
    public int Volume;
    public string MsgType;
    public float Distance;
}

public class BotVoiceAssignment
{
    public string BotName;
    public string VoiceId;
    public string Engine = "silero";
    public int Speed = 0;
}

public class VoiceDef
{
    public string Id;
    public string Engine; // "silero" or "piper"
    public string Lang;   // "ru", "en", "zh"
    public string Gender; // "female" or "male"
    public string NameRu;
    public string NameZh;
    public string NameEn;
    public string ModelTag;

    public string GetName()
    {
        if (TTSManager.IsRussianLanguage) return NameRu;
        if (TTSManager.IsChineseLanguage) return NameZh;
        return NameEn;
    }
}

public static class TTSManager
{
    public static TTSSettings Settings { get; private set; } = new TTSSettings();
    public static readonly Dictionary<string, BotVoiceAssignment> BotVoices = new Dictionary<string, BotVoiceAssignment>(StringComparer.OrdinalIgnoreCase);

    public static bool IsFemaleCharacter(Character character)
    {
        if (character == null || character.Info == null) return false;
        try
        {
            var infoType = character.Info.GetType();
            var gField = infoType.GetField("Gender", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            string genderStr = gField != null ? gField.GetValue(character.Info)?.ToString() ?? "" : "";
            if (string.IsNullOrEmpty(genderStr))
            {
                var gProp = infoType.GetProperty("Gender", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (gProp != null) genderStr = gProp.GetValue(character.Info)?.ToString() ?? "";
            }
            if (string.IsNullOrEmpty(genderStr))
            {
                var pProp = infoType.GetProperty("Pronouns", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (pProp != null) genderStr = pProp.GetValue(character.Info)?.ToString() ?? "";
            }
            return (genderStr.IndexOf("Female", StringComparison.OrdinalIgnoreCase) >= 0 || genderStr.IndexOf("She", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        catch { }
        return false;
    }

    public static List<Character> GetCrewBots()
    {
        var result = new List<Character>();
        try
        {
            if (GameMain.GameSession?.CrewManager != null)
            {
                foreach (var c in GameMain.GameSession.CrewManager.GetCharacters())
                {
                    if (c != null && c.IsBot && !c.IsDead && !result.Contains(c))
                    {
                        result.Add(c);
                    }
                }
            }
            if (result.Count == 0 && Character.CharacterList != null)
            {
                foreach (var c in Character.CharacterList)
                {
                    if (c != null && c.IsBot && !c.IsDead)
                    {
                        if (Character.Controlled != null && c.TeamID == Character.Controlled.TeamID)
                        {
                            if (!result.Contains(c)) result.Add(c);
                        }
                        else if (c.TeamID == CharacterTeamType.Team1)
                        {
                            if (!result.Contains(c)) result.Add(c);
                        }
                    }
                }
            }
        }
        catch { }
        return result;
    }

    public static readonly List<VoiceDef> AvailableVoiceDefs = new List<VoiceDef>
    {
        // Russian - Silero
        new VoiceDef { Id = "baya", Engine = "silero", Lang = "ru", Gender = "female", NameRu = "Байя", NameZh = "Baya", NameEn = "Baya", ModelTag = "Silero v4" },
        new VoiceDef { Id = "aidar", Engine = "silero", Lang = "ru", Gender = "male", NameRu = "Айдар", NameZh = "Aidar", NameEn = "Aidar", ModelTag = "Silero v4" },
        new VoiceDef { Id = "kseniya", Engine = "silero", Lang = "ru", Gender = "female", NameRu = "Ксения", NameZh = "Kseniya", NameEn = "Kseniya", ModelTag = "Silero v4" },
        new VoiceDef { Id = "xenia", Engine = "silero", Lang = "ru", Gender = "female", NameRu = "Ксения v2", NameZh = "Xenia v2", NameEn = "Xenia v2", ModelTag = "Silero v4" },
        new VoiceDef { Id = "eugene", Engine = "silero", Lang = "ru", Gender = "male", NameRu = "Евгений", NameZh = "Eugene", NameEn = "Eugene", ModelTag = "Silero v4" },

        // Russian - Piper
        new VoiceDef { Id = "baya", Engine = "piper", Lang = "ru", Gender = "female", NameRu = "Ирина", NameZh = "Irina", NameEn = "Irina", ModelTag = "Piper ONNX" },
        new VoiceDef { Id = "aidar", Engine = "piper", Lang = "ru", Gender = "male", NameRu = "Дмитрий", NameZh = "Dmitri", NameEn = "Dmitri", ModelTag = "Piper ONNX" },
        new VoiceDef { Id = "kseniya", Engine = "piper", Lang = "ru", Gender = "male", NameRu = "Денис", NameZh = "Denis", NameEn = "Denis", ModelTag = "Piper ONNX" },
        new VoiceDef { Id = "xenia", Engine = "piper", Lang = "ru", Gender = "male", NameRu = "Руслан", NameZh = "Ruslan", NameEn = "Ruslan", ModelTag = "Piper ONNX" },

        // English - Silero
        new VoiceDef { Id = "en_0", Engine = "silero", Lang = "en", Gender = "female", NameRu = "En 0", NameZh = "En 0", NameEn = "En 0", ModelTag = "Silero v3" },
        new VoiceDef { Id = "en_13", Engine = "silero", Lang = "en", Gender = "male", NameRu = "En 13", NameZh = "En 13", NameEn = "En 13", ModelTag = "Silero v3" },
        new VoiceDef { Id = "en_15", Engine = "silero", Lang = "en", Gender = "male", NameRu = "En 15", NameZh = "En 15", NameEn = "En 15", ModelTag = "Silero v3" },
        new VoiceDef { Id = "en_22", Engine = "silero", Lang = "en", Gender = "male", NameRu = "En 22", NameZh = "En 22", NameEn = "En 22", ModelTag = "Silero v3" },

        // English - Piper Arctic
        new VoiceDef { Id = "en_0", Engine = "piper", Lang = "en", Gender = "female", NameRu = "Arctic 0", NameZh = "Arctic 0", NameEn = "Arctic 0", ModelTag = "Piper Arctic" },
        new VoiceDef { Id = "en_1", Engine = "piper", Lang = "en", Gender = "male", NameRu = "Arctic 1", NameZh = "Arctic 1", NameEn = "Arctic 1", ModelTag = "Piper Arctic" },
        new VoiceDef { Id = "en_2", Engine = "piper", Lang = "en", Gender = "female", NameRu = "Arctic 2", NameZh = "Arctic 2", NameEn = "Arctic 2", ModelTag = "Piper Arctic" },
        new VoiceDef { Id = "en_4", Engine = "piper", Lang = "en", Gender = "female", NameRu = "Arctic 4", NameZh = "Arctic 4", NameEn = "Arctic 4", ModelTag = "Piper Arctic" },
        new VoiceDef { Id = "en_5", Engine = "piper", Lang = "en", Gender = "female", NameRu = "Arctic 5", NameZh = "Arctic 5", NameEn = "Arctic 5", ModelTag = "Piper Arctic" },
        new VoiceDef { Id = "en_6", Engine = "piper", Lang = "en", Gender = "male", NameRu = "Arctic 6", NameZh = "Arctic 6", NameEn = "Arctic 6", ModelTag = "Piper Arctic" },
        new VoiceDef { Id = "en_7", Engine = "piper", Lang = "en", Gender = "male", NameRu = "Arctic 7", NameZh = "Arctic 7", NameEn = "Arctic 7", ModelTag = "Piper Arctic" },

        // Chinese - Piper
        new VoiceDef { Id = "zh_huayan", Engine = "piper", Lang = "zh", Gender = "male", NameRu = "Хуаянь (华严)", NameZh = "华严 (标准普通话)", NameEn = "Huayan (Mandarin)", ModelTag = "Piper ONNX" },
        new VoiceDef { Id = "zh_huayan_officer", Engine = "piper", Lang = "zh", Gender = "male", NameRu = "Хуаянь - Офицер СБ", NameZh = "华严 (安全官/低音)", NameEn = "Huayan (Security Officer)", ModelTag = "Piper Custom" },
        new VoiceDef { Id = "zh_huayan_cadet", Engine = "piper", Lang = "zh", Gender = "male", NameRu = "Хуаянь - Матрос", NameZh = "华严 (水手/青年)", NameEn = "Huayan (Sailor Cadet)", ModelTag = "Piper Custom" },
        new VoiceDef { Id = "zh_xiao_ya", Engine = "piper", Lang = "zh", Gender = "female", NameRu = "Сяоя (晓雅)", NameZh = "晓雅 (温柔女声)", NameEn = "Xiaoya (Gentle Female)", ModelTag = "Piper ONNX" },
        new VoiceDef { Id = "zh_xiao_ya_medic", Engine = "piper", Lang = "zh", Gender = "female", NameRu = "Сяоя - Медик", NameZh = "晓雅 (医师/轻柔)", NameEn = "Xiaoya (Medic Soft)", ModelTag = "Piper Custom" },
        new VoiceDef { Id = "zh_chaowen", Engine = "piper", Lang = "zh", Gender = "female", NameRu = "Чаовэнь (超雯)", NameZh = "超雯 (沉稳女声)", NameEn = "Chaowen (Composed Female)", ModelTag = "Piper ONNX" },
        new VoiceDef { Id = "zh_chaowen_captain", Engine = "piper", Lang = "zh", Gender = "female", NameRu = "Чаовэнь - Капитан", NameZh = "超雯 (指挥官/庄重)", NameEn = "Chaowen (Captain)", ModelTag = "Piper Custom" },
        new VoiceDef { Id = "zh_chaowen_engineer", Engine = "piper", Lang = "zh", Gender = "female", NameRu = "Чаовэнь - Инженер", NameZh = "超雯 (技师/明朗)", NameEn = "Chaowen (Engineer)", ModelTag = "Piper Custom" },
    };

    private static readonly System.Net.Http.HttpClient httpClient = new System.Net.Http.HttpClient
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    private static readonly ConcurrentDictionary<Character, ConcurrentQueue<QueuedVoiceAudio>> characterVoiceQueues = new ConcurrentDictionary<Character, ConcurrentQueue<QueuedVoiceAudio>>();
    private static readonly ConcurrentQueue<QueuedVoiceAudio> globalVoiceQueue = new ConcurrentQueue<QueuedVoiceAudio>();

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
    public static string TTSEngine 
    { 
        get => Settings.TTSEngine; 
        set { Settings.TTSEngine = value; SaveSettings(); }
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
    public static bool EnableVoiceQueue 
    { 
        get => Settings.EnableVoiceQueue; 
        set { Settings.EnableVoiceQueue = value; SaveSettings(); }
    }
    public static bool EnableSuitMuffle 
    { 
        get => Settings.EnableSuitMuffle; 
        set { Settings.EnableSuitMuffle = value; SaveSettings(); }
    }
    public static bool EnableRadioFilter 
    { 
        get => Settings.EnableRadioFilter; 
        set { Settings.EnableRadioFilter = value; SaveSettings(); }
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
            string prefix = IsRussianLanguage ? "● Сервер: " : (IsChineseLanguage ? "● 服务器: " : "● Server: ");
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

                if (channel == null || !channel.IsPlaying)
                {
                    activeVoiceChannels.RemoveAt(i);
                }
                else if (character != null && !character.Removed)
                {
                    if (msgType != "Radio")
                    {
                        channel.Position = new Vector3(character.WorldPosition.X, character.WorldPosition.Y, 0f);
                    }
                    
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
                            catch { }
                        }

                        if (Settings.EnableSuitMuffle && msgType == "MuffledLocal") isMuffled = true;

                        if (character.AnimController != null && character.AnimController.HeadInWater)
                        {
                            if (Settings.EnableSuitMuffle)
                            {
                                bool hasSuit = IsWearingHelmetOrClosedSuit(character);
                                if (!hasSuit)
                                {
                                    isMuffled = true;
                                }
                            }
                        }

                        channel.Muffled = isMuffled;
                    }
                }
            }
        }

        if (Settings.EnableVoiceQueue)
        {
            var charKeys = new List<Character>(characterVoiceQueues.Keys);
            foreach (var ch in charKeys)
            {
                if (ch == null || ch.Removed)
                {
                    characterVoiceQueues.TryRemove(ch, out _);
                    continue;
                }

                if (characterVoiceQueues.TryGetValue(ch, out var queue) && !queue.IsEmpty)
                {
                    bool isSpeaking = false;
                    lock (activeVoiceChannels)
                    {
                        for (int i = 0; i < activeVoiceChannels.Count; i++)
                        {
                            if (activeVoiceChannels[i].Item2 == ch && activeVoiceChannels[i].Item1 != null && activeVoiceChannels[i].Item1.IsPlaying)
                            {
                                isSpeaking = true;
                                break;
                            }
                        }
                    }

                    if (!isSpeaking && queue.TryDequeue(out var nextAudio))
                    {
                        PlayWavBytes(nextAudio.Character, nextAudio.WavBytes, nextAudio.Volume, nextAudio.MsgType, nextAudio.Distance);
                    }
                }
            }

            if (!globalVoiceQueue.IsEmpty)
            {
                bool isGlobalSpeaking = false;
                lock (activeVoiceChannels)
                {
                    for (int i = 0; i < activeVoiceChannels.Count; i++)
                    {
                        if (activeVoiceChannels[i].Item2 == null && activeVoiceChannels[i].Item1 != null && activeVoiceChannels[i].Item1.IsPlaying)
                        {
                            isGlobalSpeaking = true;
                            break;
                        }
                    }
                }

                if (!isGlobalSpeaking && globalVoiceQueue.TryDequeue(out var globalAudio))
                {
                    PlayWavBytes(globalAudio.Character, globalAudio.WavBytes, globalAudio.Volume, globalAudio.MsgType, globalAudio.Distance);
                }
            }
        }
    }

    public static List<string> GetAvailableVoices()
    {
        return new List<string> { "aidar", "baya", "kseniya", "xenia", "eugene", "ru_ruslan", "ru_denis", "en_0", "en_1", "en_2", "en_3", "en_4", "en_5", "en_6", "en_7", "en_13", "en_15", "en_22" };
    }

    private static void SendTTSRequest(Character character, string text, string voice, int rate, int volume, string msgType, float distance, int volumeBoost, string engine = "")
    {
        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                string finalEngine = string.IsNullOrEmpty(engine) ? Settings.TTSEngine : engine;
                string escapedText = text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
                string radioFilterParam = Settings.EnableRadioFilter ? "true" : "false";
                string json = $"{{\"text\":\"{escapedText}\",\"voice\":\"{voice}\",\"rate\":{rate},\"volume\":{volume},\"boost\":{volumeBoost},\"msg_type\":\"{msgType}\",\"distance\":{distance.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)},\"sample_rate\":{Settings.SampleRate},\"engine\":\"{finalEngine}\",\"radio_filter\":{radioFilterParam}}}";
                var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync("http://127.0.0.1:5000/tts", content);
                if (response.IsSuccessStatusCode)
                {
                    if (response.Headers.TryGetValues("X-TTS-Fallback", out var fallbackValues))
                    {
                        foreach (var val in fallbackValues)
                        {
                            if (val != null && val.Trim().ToLower() == "true")
                            {
                                string reason = "";
                                if (response.Headers.TryGetValues("X-TTS-Fallback-Reason", out var reasonVals))
                                {
                                    reason = string.Join(" ", reasonVals);
                                }
                                NotifyFallback(reason);
                                break;
                            }
                        }
                    }

                    byte[] wavBytes = await response.Content.ReadAsByteArrayAsync();
                    if (msgType == "Preview" || !Settings.EnableVoiceQueue)
                    {
                        PlayWavBytes(character, wavBytes, volume, msgType, distance);
                    }
                    else
                    {
                        var item = new QueuedVoiceAudio
                        {
                            Character = character,
                            WavBytes = wavBytes,
                            Volume = volume,
                            MsgType = msgType,
                            Distance = distance
                        };

                        if (character != null)
                        {
                            var q = characterVoiceQueues.GetOrAdd(character, _ => new ConcurrentQueue<QueuedVoiceAudio>());
                            if (q.Count < 5) q.Enqueue(item);
                        }
                        else
                        {
                            if (globalVoiceQueue.Count < 5) globalVoiceQueue.Enqueue(item);
                        }
                    }
                }
                else
                {
                    TTSManager.Log("[BaroVoices TTS] HTTP Error: " + response.StatusCode);
                }
            }
            catch (Exception e)
            {
                TTSManager.Log("[BaroVoices TTS] Network TTS Error: " + e.Message);
            }
        });
    }

    private static DateTime lastFallbackNoticeTime = DateTime.MinValue;

    public static void NotifyFallback(string reason = "")
    {
        RunOnMainThread(() =>
        {
            try
            {
                if ((DateTime.UtcNow - lastFallbackNoticeTime).TotalSeconds < 20) return;
                lastFallbackNoticeTime = DateTime.UtcNow;

                string msg = GetLoc(
                    "[BaroVoices TTS] [!] Ошибка Piper: голос временно переключён на Silero.",
                    "[BaroVoices TTS] [!] Piper 合成异常：已自动切换至 Silero 引擎。",
                    "[BaroVoices TTS] [!] Piper synthesis failed: temporarily fallen back to Silero."
                );

                try
                {
                    GUI.AddMessage(msg, Color.Orange, 6.0f);
                }
                catch { }

                try
                {
                    ChatBox chatBox = GameMain.Client?.ChatBox ?? GameMain.GameSession?.CrewManager?.ChatBox;
                    if (chatBox != null)
                    {
                        chatBox.AddMessage(ChatMessage.Create("", msg, ChatMessageType.Server, null));
                    }
                }
                catch { }

                Log("[BaroVoices TTS] Fallback notification sent: " + msg);
            }
            catch (Exception ex)
            {
                Log("[BaroVoices TTS] NotifyFallback error: " + ex.Message);
            }
        });
    }

    public static string ServerStatusText = "Checking...";
    public static bool IsServerRunning = false;
    public static GUITextBlock StatusLabelRef = null;
    public static bool IsRussianLanguage = false;
    public static bool IsChineseLanguage = false;

    public static string GetLoc(string ru, string zh, string en)
    {
        if (IsRussianLanguage) return ru;
        if (IsChineseLanguage) return zh;
        return en;
    }
    private static float checkServerTimer = 0f;

    public static async void CheckServerStatusAsync()
    {
        try
        {
            var response = await httpClient.GetAsync("http://127.0.0.1:5000/voices");
            IsServerRunning = response.IsSuccessStatusCode;
            ServerStatusText = IsServerRunning ? (IsRussianLanguage ? "Работает (OK)" : "Running (OK)") : (IsRussianLanguage ? "Не запущен" : "Not running");
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
            TTSManager.Log($"[BaroVoices TTS] PlayWavBytes: Saved temp file {tempFile}. Volume: {safeVolume}");

            var sound = GameMain.SoundManager.LoadSound(tempFile, false);
            if (sound != null)
            {
                TTSManager.Log("[BaroVoices TTS] Sound loaded. Waiting 150ms before Play()...");
                Task.Delay(150).ContinueWith(_ => {
                    try
                    {
                        float gain = (safeVolume / 100f) * 2.5f;
                        Vector2 pos = character != null ? character.WorldPosition : (Character.Controlled != null ? Character.Controlled.WorldPosition : Vector2.Zero);
                        
                        var channel = (msgType == "Radio" || msgType == "Preview" || character == null) ? sound.Play(gain) : sound.Play(gain, 1500f, pos);
                        if (channel != null)
                        {
                            if (msgType == "Preview") channel.Muffled = false;
                            else if (Settings.EnableSuitMuffle && msgType == "MuffledLocal") channel.Muffled = true;
                            else channel.Muffled = false;
                            
                            lock (activeVoiceChannels) { activeVoiceChannels.Add(Tuple.Create(channel, character, msgType)); }
                            TTSManager.Log("[BaroVoices TTS] SUCCESS: Audio channel started playing.");
                        }
                        else
                        {
                            TTSManager.Log("[BaroVoices TTS] WARNING: sound.Play() returned null! Retrying in 250ms...");
                            Task.Delay(250).ContinueWith(__ => {
                                try
                                {
                                    float retryGain = (safeVolume / 100f) * 2.5f;
                                    Vector2 retryPos = character != null ? character.WorldPosition : (Character.Controlled != null ? Character.Controlled.WorldPosition : Vector2.Zero);
                                    var retryChannel = (msgType == "Radio" || msgType == "Preview" || character == null) ? sound.Play(retryGain) : sound.Play(retryGain, 1500f, retryPos);
                                    if (retryChannel != null)
                                    {
                                        if (msgType == "Preview") retryChannel.Muffled = false;
                                        else if (Settings.EnableSuitMuffle && msgType == "MuffledLocal") retryChannel.Muffled = true;
                                        else retryChannel.Muffled = false;
                                        
                                        lock (activeVoiceChannels) { activeVoiceChannels.Add(Tuple.Create(retryChannel, character, msgType)); }
                                        TTSManager.Log("[BaroVoices TTS] SUCCESS: Audio channel started playing on retry.");
                                    }
                                    else
                                    {
                                        TTSManager.Log("[BaroVoices TTS] ERROR: sound.Play() failed on retry.");
                                    }
                                }
                                catch (Exception ex2)
                                {
                                    TTSManager.Log("[BaroVoices TTS] Retry Play Error: " + ex2.Message);
                                }
                            });
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

            Task.Delay(15000).ContinueWith(_ => {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
            });
        }
        catch (Exception ex)
        {
            TTSManager.Log("[BaroVoices TTS] PlayWavBytes Error: " + ex.Message);
        }
    }

    private static void ParseEmotions(string text, ref int volumeBoost)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        
        int upperCount = 0;
        int letterCount = 0;
        foreach (char c in text)
        {
            if (char.IsLetter(c))
            {
                letterCount++;
                if (char.IsUpper(c)) upperCount++;
            }
        }
        

        if (letterCount > 3 && upperCount > letterCount * 0.6f)
        {
            volumeBoost += 35; 
        }
    }

    public static bool IsWearingHelmetOrClosedSuit(Character character)
    {
        if (character == null) return false;

        try
        {
            // 1. If character has their face covered by status effects (helmets, masks, vanilla suits)
            try
            {
                if (character.HideFace) return true;
            }
            catch { }

            if (character.Inventory == null) return false;

            // 2. Check Head slot directly for diving helmet, diving mask, or breathing gear
            Item headItem = null;
            try
            {
                headItem = character.Inventory.GetItemInLimbSlot(InvSlotType.Head);
            }
            catch { }

            if (headItem != null)
            {
                if (headItem.HasTag("divinghelmet") || headItem.HasTag("deepdiving") || 
                    headItem.HasTag("diving") || headItem.HasTag("divingmask") || 
                    headItem.HasTag("mask") || headItem.HasTag("divingsuit"))
                {
                    return true;
                }
            }

            // 3. Check OuterClothes (Suit) slot
            Item outerItem = null;
            try
            {
                outerItem = character.Inventory.GetItemInLimbSlot(InvSlotType.OuterClothes);
            }
            catch { }

            if (outerItem != null)
            {
                if (outerItem.HasTag("divingsuit") || outerItem.HasTag("deepdiving") || outerItem.HasTag("deepdivinglarge"))
                {
                    // Check if this outer suit has an integrated head cover (like vanilla suits)
                    // or is a modular body suit without head coverage (like Tidebreakers)
                    try
                    {
                        var wearable = outerItem.GetComponent<Barotrauma.Items.Components.Wearable>();
                        if (wearable != null)
                        {
                            var limbField = wearable.GetType().GetField("limbType", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                            if (limbField != null && limbField.GetValue(wearable) is LimbType[] limbs)
                            {
                                if (Array.IndexOf(limbs, LimbType.Head) >= 0)
                                {
                                    return true; // Suit has integrated head/helmet piece
                                }
                                return false; // Modular suit body without head piece (Tidebreakers)
                            }
                        }
                    }
                    catch { }

                    // Fallback: only consider muffled if head is also covered
                    if (headItem != null) return true;
                }
            }
        }
        catch { }

        return false;
    }

    private static void CheckCharacterState(Character character, ref int rate, ref int volumeBoost, ref string msgType)
    {
        if (character == null) return;

        // 1. Health & Oxygen Immersion
        try 
        {
            // Low health (< 30%): quieter and slightly slower (exhausted/weak voice)
            if (character.HealthPercentage < 30f && character.HealthPercentage > 0f)
            {
                volumeBoost -= 30;
                rate -= 2;
            }

            // Low Oxygen / Suffocation (< 40%): gasping for air
            if (character.Oxygen < 40f && character.Oxygen > 0f)
            {
                volumeBoost -= 20;
                rate -= 1;
            }
        }
        catch { }

        // 2. Suit & Water Muffling
        try 
        {
            if (Settings.EnableSuitMuffle)
            {
                if (character.AnimController != null && character.AnimController.HeadInWater)
                {
                    if (msgType != "Radio") msgType = "MuffledLocal";
                }
                else if (IsWearingHelmetOrClosedSuit(character))
                {
                    if (msgType != "Radio") msgType = "MuffledLocal";
                }
            }
        }
        catch { }
    }

    public static string GetDeterministicBotVoice(string charName, bool isFemale)
    {
        int hash = Math.Abs(charName.GetHashCode());
        if (IsChineseLanguage)
        {
            if (isFemale)
            {
                string[] zhFemale = { "zh_xiao_ya", "zh_xiao_ya_medic", "zh_chaowen", "zh_chaowen_captain", "zh_chaowen_engineer" };
                return zhFemale[hash % zhFemale.Length];
            }
            else
            {
                string[] zhMale = { "zh_huayan", "zh_huayan_officer", "zh_huayan_cadet" };
                return zhMale[hash % zhMale.Length];
            }
        }
        if (isFemale)
        {
            string[] sileroFemale = { "baya", "kseniya", "xenia" };
            return sileroFemale[hash % sileroFemale.Length];
        }
        else
        {
            string[] sileroMale = { "aidar", "eugene" };
            return sileroMale[hash % sileroMale.Length];
        }
    }

    public static void Speak(Character character, string text, string msgType = "Default")
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (character == null) return;
        if (text.StartsWith("[BaroVoices", StringComparison.OrdinalIgnoreCase) || text.StartsWith("[TTS", StringComparison.OrdinalIgnoreCase)) return;
        if (msgType == "Server" || msgType == "MessageBox" || msgType == "Console" || msgType == "ServerMessageBox") return;
        if (character.IsBot && !Settings.EnableBotTTS) return;
        string charName = character.Name;

        if (character.IsBot)
        {
            string botVoice = "aidar";
            string botEngine = "silero";
            int botSpeed = 0;

            if (BotVoices.TryGetValue(charName, out var assignment) && assignment != null)
            {
                botVoice = string.IsNullOrEmpty(assignment.VoiceId) ? "aidar" : assignment.VoiceId;
                botEngine = string.IsNullOrEmpty(assignment.Engine) ? "silero" : assignment.Engine;
                botSpeed = assignment.Speed;
            }
            else if (Settings.EnableUniqueVoices)
            {
                bool isFemale = IsFemaleCharacter(character);
                botVoice = GetDeterministicBotVoice(charName, isFemale);
                botEngine = (IsChineseLanguage || botVoice.StartsWith("zh_")) ? "piper" : "silero";
            }

            float botDistance = 0f;
            Vector2 lPos = Character.Controlled != null ? Character.Controlled.WorldPosition : (GameMain.GameScreen.Cam != null ? GameMain.GameScreen.Cam.WorldViewCenter : Vector2.Zero);
            if (lPos != Vector2.Zero)
            {
                botDistance = Vector2.Distance(lPos, character.WorldPosition);
            }

            if (msgType != "Radio" && botDistance > 4000f)
            {
                TTSManager.Log($"[BaroVoices TTS] Skipped bot generation for {charName}. Too far away ({botDistance}).");
                return;
            }

            int finalRate = Settings.BaseRate + botSpeed;
            int finalVolume = Settings.GlobalVolume;
            int finalBoost = Settings.VolumeBoost;
            string finalMsgType = msgType;

            ParseEmotions(text, ref finalBoost);
            CheckCharacterState(character, ref finalRate, ref finalBoost, ref finalMsgType);

            SendTTSRequest(character, text, botVoice, finalRate, finalVolume, finalMsgType, botDistance, finalBoost, botEngine);
            return;
        }

        string voice = Settings.VoiceName;
        if (string.IsNullOrEmpty(voice)) voice = "baya";
        
        if (character != null && character == Character.Controlled)
        {
            voice = Settings.VoiceName;
            if (string.IsNullOrEmpty(voice)) voice = "baya";
        }
        else if (Settings.EnableUniqueVoices)
        {
            int hash = character != null ? character.ID : Math.Abs(charName.GetHashCode());
            if (character != null && character.Info != null)
            {
                if (IsFemaleCharacter(character))
                {
                    string[] femaleVoices = { "baya", "kseniya", "xenia" };
                    voice = femaleVoices[hash % femaleVoices.Length];
                }
                else
                {
                    string[] maleVoices = { "aidar", "eugene" };
                    voice = maleVoices[hash % maleVoices.Length];
                }
            }
            else
            {
                string[] maleVoices = { "aidar", "eugene" };
                voice = maleVoices[hash % maleVoices.Length];
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

        int pFinalRate = Settings.BaseRate;
        int pFinalVolume = Settings.GlobalVolume;
        int pFinalBoost = Settings.VolumeBoost;
        string pFinalMsgType = msgType;

        ParseEmotions(text, ref pFinalBoost);
        CheckCharacterState(character, ref pFinalRate, ref pFinalBoost, ref pFinalMsgType);

        SendTTSRequest(character, text, voice, pFinalRate, pFinalVolume, pFinalMsgType, distance, pFinalBoost);
    }

    public static void SpeakWithCustom(Character character, string text, string customVoice, int customRate, string msgType = "Default", string customEngine = "")
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (character == null) return;
        if (text.StartsWith("[BaroVoices", StringComparison.OrdinalIgnoreCase) || text.StartsWith("[TTS", StringComparison.OrdinalIgnoreCase)) return;
        if (msgType == "Server" || msgType == "MessageBox" || msgType == "Console" || msgType == "ServerMessageBox") return;
        if (character.IsBot && !Settings.EnableBotTTS) return;
        
        string voice = string.IsNullOrEmpty(customVoice) ? "baya" : customVoice;
        string engine = customEngine;
        if (character.IsBot && string.IsNullOrEmpty(engine))
        {
            engine = "silero";
        }
        
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

        int finalRate = Settings.BaseRate + customRate;
        int finalVolume = Settings.GlobalVolume;
        int finalBoost = Settings.VolumeBoost;
        string finalMsgType = msgType;

        ParseEmotions(text, ref finalBoost);
        CheckCharacterState(character, ref finalRate, ref finalBoost, ref finalMsgType);

        SendTTSRequest(character, text, voice, finalRate, finalVolume, finalMsgType, distance, finalBoost, engine);
    }

    public static void PreviewVoice(string text, string voice, int speed, string engine)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        int finalRate = Settings.BaseRate + speed;
        int finalVolume = Settings.GlobalVolume;
        int finalBoost = Settings.VolumeBoost;
        string finalEngine = string.IsNullOrEmpty(engine) ? Settings.TTSEngine : engine;
        string finalVoice = string.IsNullOrEmpty(voice) ? "baya" : voice;

        TTSManager.Log($"[BaroVoices TTS] PreviewVoice: text='{text}', voice='{finalVoice}', engine='{finalEngine}'");
        SendTTSRequest(null, text, finalVoice, finalRate, finalVolume, "Preview", 0f, finalBoost, finalEngine);
    }

    public static void Initialize()
    {
        LoadSettings();
        LoadBotVoices();
    }

    public static bool IsMacOS()
    {
        try
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
                return true;
        }
        catch { }
        return System.Environment.OSVersion.Platform == PlatformID.MacOSX || (System.Environment.OSVersion.Platform == PlatformID.Unix && System.IO.Directory.Exists("/Applications"));
    }

    public static bool IsUnixOrLinux()
    {
        try
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
                return true;
        }
        catch { }
        return System.Environment.OSVersion.Platform == PlatformID.Unix;
    }

    public static string GetScriptPath(string scriptName)
    {
        List<string> rootDirs = new List<string> 
        { 
            "LocalMods", 
            "WorkshopMods/Installed",
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Daedalic Entertainment GmbH", "Barotrauma", "WorkshopMods", "Installed")
        };

        try
        {
            string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(homeDir))
            {
                rootDirs.Add(System.IO.Path.Combine(homeDir, ".local", "share", "Daedalic Entertainment GmbH", "Barotrauma", "WorkshopMods", "Installed"));
                rootDirs.Add(System.IO.Path.Combine(homeDir, "Library", "Application Support", "Daedalic Entertainment GmbH", "Barotrauma", "WorkshopMods", "Installed"));
            }
        }
        catch { }

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
                        if (k == "TTSEngine") Settings.TTSEngine = v;
                        if (k == "EnableVoiceQueue" && bool.TryParse(v, out bool evq)) Settings.EnableVoiceQueue = evq;
                        if (k == "EnableSuitMuffle" && bool.TryParse(v, out bool esm)) Settings.EnableSuitMuffle = esm;
                        if (k == "EnableRadioFilter" && bool.TryParse(v, out bool erf)) Settings.EnableRadioFilter = erf;
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
                "SampleRate=" + Settings.SampleRate,
                "TTSEngine=" + Settings.TTSEngine,
                "EnableVoiceQueue=" + Settings.EnableVoiceQueue,
                "EnableSuitMuffle=" + Settings.EnableSuitMuffle,
                "EnableRadioFilter=" + Settings.EnableRadioFilter
            };
            System.IO.File.WriteAllLines("TTSModSettings.txt", lines);
        }
        catch (Exception ex)
        {
            LuaCsLogger.LogError("[BaroVoices TTS] Failed to save settings: " + ex.Message);
        }
    }

    public static void LoadBotVoices()
    {
        try
        {
            BotVoices.Clear();
            string path = "TTSBotVoices.txt";
            if (File.Exists(path))
            {
                string[] lines = File.ReadAllLines(path);
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    string[] parts = line.Split('=');
                    if (parts.Length == 2)
                    {
                        string botName = parts[0].Trim();
                        string[] vals = parts[1].Split(',');
                        string vId = vals.Length > 0 ? vals[0].Trim() : "aidar";
                        string eng = vals.Length > 1 ? vals[1].Trim() : "silero";
                        int spd = 0;
                        if (vals.Length > 2) int.TryParse(vals[2].Trim(), out spd);
                        BotVoices[botName] = new BotVoiceAssignment
                        {
                            BotName = botName,
                            VoiceId = vId,
                            Engine = string.IsNullOrEmpty(eng) ? "silero" : eng,
                            Speed = spd
                        };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            TTSManager.Log("[BaroVoices TTS] LoadBotVoices Error: " + ex.Message);
        }
    }

    public static void SaveBotVoices()
    {
        try
        {
            var lines = new List<string> { "# BaroVoices TTS - Bot Voice Catalog Assignments", "# BotName=VoiceId,Engine,Speed" };
            foreach (var kvp in BotVoices)
            {
                if (kvp.Value != null)
                {
                    lines.Add($"{kvp.Key}={kvp.Value.VoiceId},{kvp.Value.Engine},{kvp.Value.Speed}");
                }
            }
            File.WriteAllLines("TTSBotVoices.txt", lines);
        }
        catch (Exception ex)
        {
            LuaCsLogger.LogError("[BaroVoices TTS] Failed to save bot voices: " + ex.Message);
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

    public static string GetModPath(string relativePath)
    {
        try
        {
            string p1 = Path.Combine("LocalMods", "BaroVoices TTS", relativePath);
            if (File.Exists(p1) || Directory.Exists(p1)) return p1;

            string asmLoc = Path.GetDirectoryName(typeof(TTSModPlugin).Assembly.Location);
            if (!string.IsNullOrEmpty(asmLoc))
            {
                string p2 = Path.GetFullPath(Path.Combine(asmLoc, "..", "..", relativePath));
                if (File.Exists(p2) || Directory.Exists(p2)) return p2;
            }
        }
        catch { }

        return Path.Combine("LocalMods", "BaroVoices TTS", relativePath);
    }

    public static bool StartServerProcess()
    {
        try
        {
            int p = (int)Environment.OSVersion.Platform;
            bool isUnix = (p == 4) || (p == 6) || (p == 128);
            string scriptName = isUnix ? "start_server.sh" : "start_server.bat";
            string scriptPath = GetModPath(scriptName);

            if (!File.Exists(scriptPath))
            {
                TTSManager.Log("[BaroVoices TTS] StartServer: Script not found: " + scriptPath);
                return false;
            }

            string fullPath = Path.GetFullPath(scriptPath);
            string langArg = TTSManager.IsRussianLanguage ? "ru" : (TTSManager.IsChineseLanguage ? "zh" : "en");

            if (isUnix)
            {
                try { Process.Start("chmod", $"+x \"{fullPath}\""); } catch { }
                Process.Start(new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = $"-c \"x-terminal-emulator -e \\\"bash '{fullPath}' {langArg}\\\" || gnome-terminal -- bash '{fullPath}' {langArg} || konsole -e bash '{fullPath}' {langArg} || xfce4-terminal -e \\\"bash '{fullPath}' {langArg}\\\" || alacritty -e bash '{fullPath}' {langArg} || kitty bash '{fullPath}' {langArg} || xterm -e bash '{fullPath}' {langArg}\"",
                    UseShellExecute = false
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c start \"BaroVoices TTS Server\" \"{fullPath}\" {langArg}",
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(fullPath)
                });
            }

            TTSManager.Log("[BaroVoices TTS] StartServer: Launched " + fullPath);
            return true;
        }
        catch (Exception ex)
        {
            TTSManager.Log("[BaroVoices TTS] StartServer error: " + ex.Message);
            return false;
        }
    }

    private static int activeTab = 0;
    private static List<GUIButton> tabButtons = new List<GUIButton>();

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

            TTSManager.Log("[BaroVoices TTS] ToggleMenu: Initializing Vocal Terminal...");

            bool isRussian = false;
            bool isChinese = false;
            try
            {
                var settingsProp = typeof(GameMain).Assembly.GetType("Barotrauma.GameSettings")?.GetProperty("CurrentConfig");
                if (settingsProp != null)
                {
                    var config = settingsProp.GetValue(null);
                    var lang = config?.GetType().GetProperty("Language")?.GetValue(config) ?? config?.GetType().GetField("Language")?.GetValue(config);
                    if (lang != null)
                    {
                        string langStr = lang.ToString().ToLower();
                        if (langStr.Contains("ru")) isRussian = true;
                        if (langStr.Contains("chinese") || langStr.Contains("zh")) isChinese = true;
                    }
                }
            }
            catch { }
            TTSManager.IsRussianLanguage = isRussian;
            TTSManager.IsChineseLanguage = isChinese;

            // Draft copy of settings for Cancel / Apply workflow
            int draftGlobalVolume = TTSManager.GlobalVolume;
            int draftVolumeBoost = TTSManager.VolumeBoost;
            int draftBaseRate = TTSManager.BaseRate;
            bool draftEnableUniqueVoices = TTSManager.EnableUniqueVoices;
            bool draftEnableBotTTS = TTSManager.EnableBotTTS;
            bool draftEnableVoiceQueue = TTSManager.EnableVoiceQueue;
            bool draftEnableSuitMuffle = TTSManager.EnableSuitMuffle;
            bool draftEnableRadioFilter = TTSManager.EnableRadioFilter;
            string draftTTSEngine = TTSManager.TTSEngine;
            int draftSampleRate = TTSManager.SampleRate;
            string draftVoiceName = TTSManager.VoiceName;
            int draftMySpeed = TTSManager.MySpeed;

            string iconAtlasPath = GetModPath(Path.Combine("Content", "UI", "BaroVoicesIcons_v2.png"));
            bool hasIcons = File.Exists(iconAtlasPath);

            RectTransform parentTransform = GUI.PauseMenu?.RectTransform;
            currentFrame = new GUIFrame(new RectTransform(new Vector2(0.70f, 0.78f), parentTransform, Anchor.Center), "GUIFrame");
            currentFrame.CanBeFocused = true;

            var outerLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.96f, 0.95f), currentFrame.RectTransform, Anchor.Center))
            {
                Stretch = false,
                RelativeSpacing = 0.015f
            };

            // Top Header Console Bar
            var headerRow = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.06f), outerLayout.RectTransform), isHorizontal: true)
            {
                Stretch = false,
                RelativeSpacing = 0.02f
            };

            string terminalTitle = TTSManager.GetLoc("BAROVOICES TTS // НАСТРОЙКИ МОДА", "BAROVOICES TTS // 模组设置", "BAROVOICES TTS // MOD SETTINGS");
            var titleBlock = new GUITextBlock(new RectTransform(new Vector2(0.55f, 1f), headerRow.RectTransform), terminalTitle, textAlignment: Alignment.CenterLeft)
            {
                TextColor = new Color(80, 225, 210)
            };

            string initPrefix = isRussian ? "● Сервер: " : (isChinese ? "● 服务器: " : "● Server: ");
            var headerStatus = new GUITextBlock(new RectTransform(new Vector2(0.45f, 1f), headerRow.RectTransform), initPrefix + TTSManager.ServerStatusText, textAlignment: Alignment.CenterRight);
            headerStatus.TextColor = TTSManager.IsServerRunning ? Color.LimeGreen : Color.Tomato;
            TTSManager.StatusLabelRef = headerStatus;

            // Main Split: Left Column (28%) & Right Column (72%)
            var bodySplit = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.92f), outerLayout.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            // LEFT COLUMN
            var leftCol = new GUILayoutGroup(new RectTransform(new Vector2(0.28f, 1f), bodySplit.RectTransform))
            {
                Stretch = false,
                RelativeSpacing = 0.015f
            };

            // Mod Poster Card with proper scaling inside frame
            var posterCard = new GUIFrame(new RectTransform(new Vector2(1f, 0.36f), leftCol.RectTransform), style: "InnerFrame");
            try
            {
                string posterPath = GetModPath("BaroVoices TTS.png");
                if (File.Exists(posterPath))
                {
                    var posterSprite = new Sprite(posterPath, Vector2.Zero);
                    var posterImg = new GUIImage(new RectTransform(new Vector2(0.94f, 0.94f), posterCard.RectTransform, Anchor.Center), posterSprite, scaleToFit: true);
                    posterImg.CanBeFocused = false;
                }
            }
            catch { }

            var authorLabel = new GUITextBlock(new RectTransform(new Vector2(1f, 0.04f), leftCol.RectTransform), "BaroVoices TTS v1.2.3 • by VORON", textAlignment: Alignment.Center)
            {
                TextColor = Color.Gold
            };

            // Left Info Card
            var leftInfoCard = new GUIFrame(new RectTransform(new Vector2(1f, 0.50f), leftCol.RectTransform), style: "InnerFrame");
            var leftInfoLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.92f, 0.92f), leftInfoCard.RectTransform, Anchor.Center))
            {
                Stretch = false,
                RelativeSpacing = 0.02f
            };

            string statusHead = TTSManager.GetLoc("ИНФОРМАЦИЯ", "系统信息", "INFORMATION");
            new GUITextBlock(new RectTransform(new Vector2(1f, 0.12f), leftInfoLayout.RectTransform), statusHead, textAlignment: Alignment.CenterLeft)
            {
                TextColor = Color.White
            };

            string engineInfoText = TTSManager.GetLoc($"Движок: {draftTTSEngine.ToUpper()}", $"引擎：{draftTTSEngine.ToUpper()}", $"Engine: {draftTTSEngine.ToUpper()}");
            new GUITextBlock(new RectTransform(new Vector2(1f, 0.12f), leftInfoLayout.RectTransform), engineInfoText, textAlignment: Alignment.CenterLeft)
            {
                TextColor = new Color(160, 220, 215)
            };

            var leftServerBtn = new GUIButton(new RectTransform(new Vector2(1f, 0.18f), leftInfoLayout.RectTransform), TTSManager.GetLoc("ЗАПУСТИТЬ СЕРВЕР", "启动服务器", "START SERVER"), Alignment.Center, "GUIButton");
            leftServerBtn.TextColor = Color.LightGreen;
            leftServerBtn.ToolTip = TTSManager.GetLoc("Запустить локальный сервер синтеза речи (start_server.bat).", "启动本地TTS语音服务器。", "Launch local TTS speech server (start_server.bat).");
            leftServerBtn.OnClicked = (b, ud) =>
            {
                if (StartServerProcess())
                {
                    leftServerBtn.Text = TTSManager.GetLoc("Запуск...", "正在启动...", "Starting...");
                    TTSManager.CheckServerStatusAsync();
                }
                return true;
            };

            string configTip = TTSManager.GetLoc(
                "Конфиг: TTSModSettings.txt\n\nПри сбое Piper синтез автоматически переключается на Silero.",
                "配置：TTSModSettings.txt\n\nPiper合成失败时将自动无缝降级为Silero。",
                "Config: TTSModSettings.txt\n\nIf Piper fails, synthesis automatically falls back to Silero."
            );
            new GUITextBlock(new RectTransform(new Vector2(1f, 0.50f), leftInfoLayout.RectTransform), configTip, textAlignment: Alignment.TopLeft, wrap: true)
            {
                TextColor = Color.LightSlateGray
            };

            // Left Links Row
            var leftLinks = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.07f), leftCol.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.04f
            };

            var boostyBtn = new GUIButton(new RectTransform(new Vector2(0.48f, 1f), leftLinks.RectTransform), "Boosty", Alignment.Center, "GUIButton");
            boostyBtn.TextColor = Color.Gold;
            boostyBtn.ToolTip = TTSManager.GetLoc("Поддержать автора на Boosty", "在Boosty上支持作者", "Support author on Boosty");
            boostyBtn.OnClicked = (b, ud) =>
            {
                try { Process.Start(new ProcessStartInfo { FileName = "https://boosty.to/voron227", UseShellExecute = true }); } catch { }
                return true;
            };

            var githubBtn = new GUIButton(new RectTransform(new Vector2(0.48f, 1f), leftLinks.RectTransform), "GitHub", Alignment.Center, "GUIButton");
            githubBtn.TextColor = Color.LightCyan;
            githubBtn.ToolTip = TTSManager.GetLoc("Репозиторий проекта на GitHub", "访问GitHub源码仓库", "Visit GitHub repository");
            githubBtn.OnClicked = (b, ud) =>
            {
                try { Process.Start(new ProcessStartInfo { FileName = "https://github.com/VORON2272/BaroVoices-TTS", UseShellExecute = true }); } catch { }
                return true;
            };

            // RIGHT COLUMN
            var rightCol = new GUILayoutGroup(new RectTransform(new Vector2(0.72f, 1f), bodySplit.RectTransform))
            {
                Stretch = false,
                RelativeSpacing = 0.015f
            };

            // Top Tab Buttons
            var tabBar = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.08f), rightCol.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            tabButtons.Clear();

            // Right Central Card for Content
            var contentCard = new GUIFrame(new RectTransform(new Vector2(1f, 0.81f), rightCol.RectTransform), style: "InnerFrame");

            // Slider builder helper in Plag / Terminal style
            void AddSlider(GUILayoutGroup parent, string title, float curVal, float minVal, float maxVal, Func<float, string> format, Action<float> onMove, string tip = "", bool isBoost = false)
            {
                var sGroup = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.18f), parent.RectTransform)) { RelativeSpacing = 0.02f };
                new GUITextBlock(new RectTransform(new Vector2(1f, 0.40f), sGroup.RectTransform), title, textAlignment: Alignment.CenterLeft)
                {
                    TextColor = Color.LightGray
                };

                var row = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.55f), sGroup.RectTransform), isHorizontal: true) { RelativeSpacing = 0.03f };

                var sb = new GUIScrollBar(new RectTransform(new Vector2(0.72f, 1f), row.RectTransform), barSize: 0.08f, style: "GUISlider")
                {
                    BarScroll = Math.Max(0f, Math.Min(1f, (curVal - minVal) / (maxVal - minVal)))
                };
                if (!string.IsNullOrEmpty(tip)) sb.ToolTip = tip;

                var lbl = new GUITextBlock(new RectTransform(new Vector2(0.25f, 1f), row.RectTransform), format(curVal), textAlignment: Alignment.CenterRight);
                lbl.TextColor = (isBoost && curVal > 100f) ? new Color(255, 195, 60) : new Color(64, 210, 195);

                sb.OnMoved = (scrollbar, scroll) =>
                {
                    float calculated = minVal + scroll * (maxVal - minVal);
                    onMove(calculated);
                    lbl.Text = format(calculated);
                    lbl.TextColor = (isBoost && calculated > 100f) ? new Color(255, 195, 60) : new Color(64, 210, 195);
                    return true;
                };
            }

            Action renderActiveTab = null;

            // Tab 0: Gameplay / General Settings
            Action showGameplayTab = () =>
            {
                contentCard.ClearChildren();
                var layout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), contentCard.RectTransform, Anchor.Center)) { RelativeSpacing = 0.04f };

                new GUITextBlock(new RectTransform(new Vector2(1f, 0.08f), layout.RectTransform), TTSManager.GetLoc("ОСНОВНЫЕ НАСТРОЙКИ", "基础设置", "GENERAL SETTINGS"), textAlignment: Alignment.CenterLeft)
                {
                    TextColor = Color.White
                };

                var box1 = new GUITickBox(new RectTransform(new Vector2(1f, 0.12f), layout.RectTransform), TTSManager.GetLoc("Авто-выбор голосов для экипажа", "为船员与玩家自动分配音色", "Auto-assign voices for crew"))
                {
                    Selected = draftEnableUniqueVoices,
                    ToolTip = TTSManager.GetLoc("Каждый бот и игрок получит подходящий уникальный голос.", "根据身份与性别为角色自动指派合适的声音。", "Assigns fitting random voices to crew members.")
                };
                box1.OnSelected = (tb) => { draftEnableUniqueVoices = tb.Selected; return true; };

                var box2 = new GUITickBox(new RectTransform(new Vector2(1f, 0.12f), layout.RectTransform), TTSManager.GetLoc("Озвучивать ботов (Bot TTS)", "启用机器人语音 (Bot TTS)", "Enable Bot TTS"))
                {
                    Selected = draftEnableBotTTS,
                    ToolTip = TTSManager.GetLoc("Синтезирует речь ботов экипажа и NPC при отправке команд и реплик.", "朗读AI船员与NPC发出的文字指令和对话。", "Synthesizes speech for AI bots and NPC dialogues.")
                };
                box2.OnSelected = (tb) => { draftEnableBotTTS = tb.Selected; return true; };

                var box3 = new GUITickBox(new RectTransform(new Vector2(1f, 0.12f), layout.RectTransform), TTSManager.GetLoc("Очередь сообщений (Anti-overlap)", "语音队列模式 (防重叠)", "Voice Queue (Anti-overlap)"))
                {
                    Selected = draftEnableVoiceQueue,
                    ToolTip = TTSManager.GetLoc("Фразы одного персонажа воспроизводятся по очереди, не перебивая друг друга.", "同一角色的语句按顺序排队播放，防止音频互相叠音刺耳。", "Sequential playback per character to prevent overlapping audio.")
                };
                box3.OnSelected = (tb) => { draftEnableVoiceQueue = tb.Selected; return true; };

                new GUITextBlock(new RectTransform(new Vector2(1f, 0.35f), layout.RectTransform),
                    TTSManager.GetLoc(
                        "Подсказка: Данные настройки влияют на воспроизведение речи на вашей подлодке. Изменения сохраняются локально.",
                        "提示：此配置将应用于您潜艇上接收到的所有TTS语音。配置保存在本地。",
                        "Tip: These settings affect voice messages playback on your submarine. Changes are saved locally."
                    ),
                    textAlignment: Alignment.TopLeft, wrap: true)
                {
                    TextColor = Color.LightSlateGray
                };
            };

            // Current filter state for Tab 1
            string activeLangFilter = isRussian ? "ru" : (isChinese ? "zh" : "all");
            string activeEngineFilter = "all"; // "all", "silero", "piper"

            // Tab 1: My Voice
            Action showVoiceTab = null;
            showVoiceTab = () =>
            {
                contentCard.ClearChildren();
                var layout = new GUILayoutGroup(new RectTransform(new Vector2(0.96f, 0.96f), contentCard.RectTransform, Anchor.Center)) { RelativeSpacing = 0.015f };

                // 1. Top row: Title + Current Selected Voice Info
                var topRow = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.06f), layout.RectTransform), isHorizontal: true);
                new GUITextBlock(new RectTransform(new Vector2(0.35f, 1f), topRow.RectTransform), TTSManager.GetLoc("МОЙ ГОЛОС", "我的声音", "MY VOICE"), textAlignment: Alignment.CenterLeft)
                {
                    TextColor = Color.White
                };

                VoiceDef currentDef = TTSManager.AvailableVoiceDefs.Find(vd => vd.Id == draftVoiceName && vd.Engine == draftTTSEngine);
                if (currentDef == null)
                {
                    currentDef = TTSManager.AvailableVoiceDefs.Find(vd => vd.Id == draftVoiceName) 
                              ?? TTSManager.AvailableVoiceDefs.Find(vd => vd.Engine == draftTTSEngine) 
                              ?? TTSManager.AvailableVoiceDefs[0];
                }

                string currentInfoText = TTSManager.GetLoc("Выбран: ", "当前选择: ", "Selected: ") 
                    + $"{currentDef.GetName()} [{(currentDef.Engine == "piper" ? "Piper" : "Silero")}, {currentDef.Lang.ToUpper()}]";

                new GUITextBlock(new RectTransform(new Vector2(0.65f, 1f), topRow.RectTransform), currentInfoText, textAlignment: Alignment.CenterRight)
                {
                    TextColor = new Color(80, 225, 210)
                };

                // 2. Filter Row: Language Chips + Engine Chips
                var filterRow = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.065f), layout.RectTransform), isHorizontal: true) { RelativeSpacing = 0.01f };

                var langFilters = new (string key, string ru, string zh, string en)[]
                {
                    ("all", "Все", "全部", "All"),
                    ("ru", "RU", "俄语 (RU)", "RU"),
                    ("en", "EN", "英语 (EN)", "EN"),
                    ("zh", "ZH", "中文 (ZH)", "ZH")
                };

                foreach (var lf in langFilters)
                {
                    bool isSelected = activeLangFilter == lf.key;
                    var btn = new GUIButton(new RectTransform(new Vector2(0.095f, 1f), filterRow.RectTransform), TTSManager.GetLoc(lf.ru, lf.zh, lf.en), Alignment.Center, "GUIButton");
                    if (isSelected)
                    {
                        btn.Color = new Color(20, 80, 70);
                        btn.TextColor = Color.Turquoise;
                    }
                    else
                    {
                        btn.TextColor = Color.LightGray;
                    }
                    string k = lf.key;
                    btn.OnClicked = (b, ud) =>
                    {
                        activeLangFilter = k;
                        showVoiceTab();
                        return true;
                    };
                }

                // Spacer
                new GUIFrame(new RectTransform(new Vector2(0.03f, 1f), filterRow.RectTransform), style: null);

                var engFilters = new (string key, string ru, string zh, string en, float width)[]
                {
                    ("all", "Все движки", "全部引擎", "All Engines", 0.18f),
                    ("silero", "Silero", "Silero", "Silero", 0.14f),
                    ("piper", "Piper", "Piper", "Piper", 0.14f)
                };

                foreach (var ef in engFilters)
                {
                    bool isSelected = activeEngineFilter == ef.key;
                    var btn = new GUIButton(new RectTransform(new Vector2(ef.width, 1f), filterRow.RectTransform), TTSManager.GetLoc(ef.ru, ef.zh, ef.en), Alignment.Center, "GUIButton");
                    if (isSelected)
                    {
                        btn.Color = ef.key == "piper" ? new Color(20, 70, 90) : (ef.key == "silero" ? new Color(80, 60, 20) : new Color(40, 50, 60));
                        btn.TextColor = ef.key == "piper" ? Color.LightSkyBlue : (ef.key == "silero" ? Color.Orange : Color.Turquoise);
                    }
                    else
                    {
                        btn.TextColor = Color.LightGray;
                    }
                    string k = ef.key;
                    btn.OnClicked = (b, ud) =>
                    {
                        activeEngineFilter = k;
                        showVoiceTab();
                        return true;
                    };
                }

                // 3. Scrollable List of Voice Cards
                var listBox = new GUIListBox(new RectTransform(new Vector2(1f, 0.52f), layout.RectTransform));
                listBox.Spacing = (int)(4 * GUI.Scale);

                var displayedVoices = TTSManager.AvailableVoiceDefs.FindAll(vd =>
                {
                    if (activeLangFilter != "all" && vd.Lang != activeLangFilter) return false;
                    if (activeEngineFilter != "all" && vd.Engine != activeEngineFilter) return false;
                    return true;
                });

                if (displayedVoices.Count == 0)
                {
                    var emptyCard = new GUIFrame(new RectTransform(new Vector2(1f, 0.25f), listBox.Content.RectTransform), style: "InnerFrame");
                    emptyCard.Color = new Color(22, 26, 32);
                    new GUITextBlock(new RectTransform(Vector2.One, emptyCard.RectTransform), 
                        TTSManager.GetLoc("Нет доступных голосов для выбранных фильтров", "所选筛选条件下无可用音色", "No voices available for selected filters"),
                        textAlignment: Alignment.Center)
                    {
                        TextColor = Color.Gray
                    };
                }

                foreach (var vDef in displayedVoices)
                {
                    bool isCurrent = (draftVoiceName == vDef.Id && draftTTSEngine == vDef.Engine);

                    var rowCard = new GUIFrame(new RectTransform(new Vector2(1f, 0.20f), listBox.Content.RectTransform), style: "InnerFrame");
                    rowCard.Color = isCurrent ? new Color(18, 50, 42) : new Color(22, 26, 32);

                    var rowLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 0.90f), rowCard.RectTransform, Anchor.Center), isHorizontal: true)
                    {
                        RelativeSpacing = 0.015f
                    };

                    string genderTag = vDef.Gender == "female" 
                        ? TTSManager.GetLoc("(Жен.)", "(女)", "(Fem)") 
                        : TTSManager.GetLoc("(Муж.)", "(男)", "(Masc)");
                    string selMark = isCurrent ? "● " : "   ";
                    string engineTag = vDef.Engine == "piper" ? "[Piper]" : "[Silero]";
                    string labelText = $"{selMark}{vDef.GetName()} {genderTag}  {engineTag} • {vDef.ModelTag}";

                    var selectBtn = new GUIButton(new RectTransform(new Vector2(0.85f, 1f), rowLayout.RectTransform), labelText, Alignment.CenterLeft, "GUIButton");
                    if (isCurrent)
                    {
                        selectBtn.Color = new Color(25, 90, 70);
                        selectBtn.TextColor = Color.White;
                    }
                    else
                    {
                        selectBtn.TextColor = Color.LightGray;
                    }

                    VoiceDef capturedDef = vDef;
                    selectBtn.OnClicked = (b, ud) =>
                    {
                        draftVoiceName = capturedDef.Id;
                        draftTTSEngine = capturedDef.Engine;
                        showVoiceTab();
                        return true;
                    };

                    var quickPrevBtn = new GUIButton(new RectTransform(new Vector2(0.12f, 1f), rowLayout.RectTransform), "", Alignment.Center, "GUIButton");
                    quickPrevBtn.ToolTip = TTSManager.GetLoc("Быстрое прослушивание этого голоса", "快速试听此音色", "Quickly preview this voice");

                    if (hasIcons)
                    {
                        try
                        {
                            var playSprite = new Sprite(iconAtlasPath, new Rectangle(384, 0, 64, 64));
                            var playImg = new GUIImage(new RectTransform(new Vector2(0.55f, 0.55f), quickPrevBtn.RectTransform, Anchor.Center), playSprite, scaleToFit: true)
                            {
                                CanBeFocused = false
                            };
                            playImg.Color = vDef.Engine == "piper" ? new Color(130, 210, 255) : new Color(255, 185, 100);
                            playImg.HoverColor = Color.White;
                            playImg.SelectedColor = Color.White;
                        }
                        catch { }
                    }
                    else
                    {
                        quickPrevBtn.Text = ">";
                        quickPrevBtn.TextColor = vDef.Engine == "piper" ? Color.LightSkyBlue : Color.Orange;
                    }

                    quickPrevBtn.OnClicked = (b, ud) =>
                    {
                        string sample = capturedDef.Lang == "ru" 
                            ? "Внимание экипажу, проверка связи!" 
                            : (capturedDef.Lang == "zh" ? "全体船员注意，无线电测试！" : "Attention crew, comms check!");
                        TTSManager.PreviewVoice(sample, capturedDef.Id, draftMySpeed, capturedDef.Engine);
                        return true;
                    };
                }

                // 4. Speech Speed Slider
                AddSlider(layout, TTSManager.GetLoc("МОЯ СКОРОСТЬ РЕЧИ", "个人语速调节", "MY SPEECH SPEED"), draftMySpeed, -10f, 10f,
                    v => (v > 0 ? "+" : "") + ((int)v).ToString(),
                    v => draftMySpeed = (int)v,
                    TTSManager.GetLoc("Индивидуальная поправка скорости речи вашего персонажа.", "你个人的发音速度修饰符。", "Personal speaking speed modifier for your character."));

                // 5. Action Buttons (Radio Test & Sync)
                var voiceActions = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.08f), layout.RectTransform), isHorizontal: true) { RelativeSpacing = 0.03f };

                var prevBtn = new GUIButton(new RectTransform(new Vector2(0.48f, 1f), voiceActions.RectTransform), TTSManager.GetLoc("> ТЕСТОВАЯ ФРАЗА (РАЦИЯ)", "> 测试对讲机发音", "> RADIO TEST PHRASE"), Alignment.Center, "GUIButton");
                prevBtn.ToolTip = TTSManager.GetLoc("Прослушать выбранный голос с эффектом рации", "试听当前所选音色的无线电效果", "Preview selected voice with radio effect");
                prevBtn.OnClicked = (b, ud) =>
                {
                    string sampleText = TTSManager.GetLoc("Внимание экипажу! Проверка связи, как слышно?", "全体船员注意！无线电通讯测试，收到请回答？", "Attention crew! Radio comms check, how do you copy?");
                    TTSManager.PreviewVoice(sampleText, draftVoiceName, draftMySpeed, draftTTSEngine);
                    return true;
                };

                var syncBtn = new GUIButton(new RectTransform(new Vector2(0.48f, 1f), voiceActions.RectTransform), TTSManager.GetLoc("СИНХРОНИЗИРОВАТЬ", "同步给队友", "SYNC WITH CREW"), Alignment.Center, "GUIButton");
                syncBtn.TextColor = Color.LightGreen;
                syncBtn.OnClicked = (b, ud) =>
                {
                    if (Character.Controlled == null)
                    {
                        syncBtn.Text = TTSManager.GetLoc("ТОЛЬКО В ИГРЕ!", "只能在游戏内！", "IN-GAME ONLY!");
                        return true;
                    }
                    string v = string.IsNullOrEmpty(draftVoiceName) ? "baya" : draftVoiceName;
                    string luaCmd = $"if SendMyVoiceSettings then SendMyVoiceSettings({TTSManager.MyPitch}, {draftMySpeed}, \"{v}\", \"{draftTTSEngine}\") end";
                    try { GameMain.LuaCs.Lua?.DoString(luaCmd); } catch { }
                    syncBtn.Text = TTSManager.GetLoc("СИНХРОНИЗИРОВАНО", "已同步", "SYNCED");
                    return true;
                };
            };

            // Tab 2: Crew Bot Voice Catalog
            Action showBotsTab = null;
            showBotsTab = () =>
            {
                contentCard.ClearChildren();
                var layout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), contentCard.RectTransform, Anchor.Center)) { RelativeSpacing = 0.02f };

                // 1. Header
                var topRow = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.07f), layout.RectTransform), isHorizontal: true) { RelativeSpacing = 0.02f };
                new GUITextBlock(new RectTransform(new Vector2(0.60f, 1f), topRow.RectTransform), TTSManager.GetLoc("КАТАЛОГ ГОЛОСОВ БОТОВ (ЭКИПАЖ)", "AI船员音色目录", "CREW BOT VOICE CATALOG"), textAlignment: Alignment.CenterLeft)
                {
                    TextColor = Color.White
                };

                // Fast action buttons in top row: Randomize Silero, Reset All
                var randSileroBtn = new GUIButton(new RectTransform(new Vector2(0.20f, 1f), topRow.RectTransform), TTSManager.GetLoc("СЛУЧАЙНЫЕ", "随机分配", "RANDOM SILERO"), Alignment.Center, "GUIButton");
                randSileroBtn.ToolTip = TTSManager.GetLoc("Назначить каждому боту случайный Silero голос по его полу", "根据性别为每名AI船员随机分配Silero音色", "Assign random Silero voice to each bot by gender");
                randSileroBtn.OnClicked = (b, ud) =>
                {
                    var crew = TTSManager.GetCrewBots();
                    var femaleSilero = new[] { "baya", "kseniya", "xenia" };
                    var maleSilero = new[] { "aidar", "eugene" };
                    var rnd = new Random();

                    var names = new HashSet<string>(TTSManager.BotVoices.Keys, StringComparer.OrdinalIgnoreCase);
                    foreach (var cb in crew) if (cb != null && !string.IsNullOrEmpty(cb.Name)) names.Add(cb.Name);

                    foreach (var name in names)
                    {
                        var cb = crew.Find(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                        bool isFem = cb != null ? TTSManager.IsFemaleCharacter(cb) : (TTSManager.BotVoices.TryGetValue(name, out var ex) && ex.VoiceId != null && (ex.VoiceId == "baya" || ex.VoiceId == "kseniya" || ex.VoiceId == "xenia"));
                        string chosenVoice = isFem ? femaleSilero[rnd.Next(femaleSilero.Length)] : maleSilero[rnd.Next(maleSilero.Length)];

                        TTSManager.BotVoices[name] = new BotVoiceAssignment
                        {
                            BotName = name,
                            VoiceId = chosenVoice,
                            Engine = "silero",
                            Speed = 0
                        };
                    }
                    showBotsTab();
                    return true;
                };

                var resetAllBotsBtn = new GUIButton(new RectTransform(new Vector2(0.18f, 1f), topRow.RectTransform), TTSManager.GetLoc("СБРОСИТЬ", "恢复默认", "RESET ALL"), Alignment.Center, "GUIButton");
                resetAllBotsBtn.ToolTip = TTSManager.GetLoc("Сбросить все назначения ботов на автоматические", "将所有船员声音重置为自动默认分配", "Reset all bot assignments to automatic defaults");
                resetAllBotsBtn.TextColor = Color.Salmon;
                resetAllBotsBtn.OnClicked = (b, ud) =>
                {
                    TTSManager.BotVoices.Clear();
                    showBotsTab();
                    return true;
                };

                // Subtitle
                new GUITextBlock(new RectTransform(new Vector2(1f, 0.045f), layout.RectTransform), TTSManager.GetLoc("Индивидуальная настройка голоса каждого бота. По умолчанию боты используют Silero.", "为每个AI船员自定义独立音色。默认统一使用Silero语音引擎。", "Customize individual voice for each bot. By default, bots use Silero engine."), textAlignment: Alignment.CenterLeft)
                {
                    TextColor = Color.Gray
                };

                // List of bots
                var botListBox = new GUIListBox(new RectTransform(new Vector2(1f, 0.72f), layout.RectTransform));
                botListBox.Spacing = (int)(4 * GUI.Scale);

                var currentCrewBots = TTSManager.GetCrewBots();
                var botNames = new List<string>();
                foreach (var cb in currentCrewBots)
                {
                    if (cb != null && !string.IsNullOrEmpty(cb.Name) && !botNames.Contains(cb.Name))
                    {
                        botNames.Add(cb.Name);
                    }
                }
                foreach (var k in TTSManager.BotVoices.Keys)
                {
                    if (!botNames.Contains(k))
                    {
                        botNames.Add(k);
                    }
                }

                if (botNames.Count == 0)
                {
                    var emptyFrame = new GUIFrame(new RectTransform(new Vector2(1f, 0.3f), botListBox.Content.RectTransform), style: "InnerFrame");
                    new GUITextBlock(new RectTransform(new Vector2(0.9f, 0.8f), emptyFrame.RectTransform, Anchor.Center), 
                        TTSManager.GetLoc("В текущем раунде не найдено ботов экипажа.\nВы можете добавить имя бота вручную в строке ниже.", "当前回合未发现AI船员。\n您可以在下方手动输入船员名字添加配置。", "No crew bots found in current round.\nYou can manually register a bot name in the field below."),
                        textAlignment: Alignment.Center)
                    {
                        TextColor = Color.LightGray
                    };
                }
                else
                {
                    var voiceList = TTSManager.AvailableVoiceDefs;
                    foreach (string bName in botNames)
                    {
                        string botName = bName;
                        var liveBot = currentCrewBots.Find(cb => cb.Name.Equals(botName, StringComparison.OrdinalIgnoreCase));
                        bool isFemale = liveBot != null 
                            ? TTSManager.IsFemaleCharacter(liveBot) 
                            : (TTSManager.BotVoices.TryGetValue(botName, out var exAssign) && (exAssign.VoiceId == "baya" || exAssign.VoiceId == "kseniya" || exAssign.VoiceId == "xenia"));

                        if (!TTSManager.BotVoices.TryGetValue(botName, out var assign) || assign == null)
                        {
                            assign = new BotVoiceAssignment
                            {
                                BotName = botName,
                                VoiceId = TTSManager.GetDeterministicBotVoice(botName, isFemale),
                                Engine = "silero",
                                Speed = 0
                            };
                            TTSManager.BotVoices[botName] = assign;
                        }

                        var botRow = new GUIFrame(new RectTransform(new Vector2(1f, 0.20f), botListBox.Content.RectTransform), style: "InnerFrame");
                        botRow.Color = liveBot != null ? new Color(20, 35, 38) : new Color(24, 26, 30);

                        var rowLayout = new GUILayoutGroup(new RectTransform(new Vector2(0.98f, 0.90f), botRow.RectTransform, Anchor.Center), isHorizontal: true)
                        {
                            RelativeSpacing = 0.015f
                        };

                        // Bot Name & Gender & Status
                        string genderTag = isFemale 
                            ? TTSManager.GetLoc("(Жен.)", "(女)", "(Fem)") 
                            : TTSManager.GetLoc("(Муж.)", "(男)", "(Masc)");
                        string statusTag = liveBot != null ? "● " : "○ ";
                        string botTitle = $"{statusTag}{botName} {genderTag}";

                        new GUITextBlock(new RectTransform(new Vector2(0.32f, 1f), rowLayout.RectTransform), botTitle, textAlignment: Alignment.CenterLeft)
                        {
                            TextColor = liveBot != null ? new Color(130, 230, 200) : Color.LightGray,
                            ToolTip = liveBot != null ? TTSManager.GetLoc("Активный бот на подлодке", "本局在线船员", "Active crew bot on submarine") : TTSManager.GetLoc("Сохраненный бот", "已保存配置", "Saved bot configuration")
                        };

                        // Stepper: < [ Voice Name ] >
                        var stepperGroup = new GUILayoutGroup(new RectTransform(new Vector2(0.48f, 1f), rowLayout.RectTransform), isHorizontal: true)
                        {
                            RelativeSpacing = 0.02f
                        };

                        int curIdx = voiceList.FindIndex(vd => vd.Id == assign.VoiceId && vd.Engine == assign.Engine);
                        if (curIdx < 0) curIdx = voiceList.FindIndex(vd => vd.Id == assign.VoiceId);
                        if (curIdx < 0) curIdx = 0;

                        var prevVoiceBtn = new GUIButton(new RectTransform(new Vector2(0.14f, 1f), stepperGroup.RectTransform), "<", Alignment.Center, "GUIButton");
                        
                        VoiceDef currentDef = voiceList[curIdx];
                        string curVoiceName = currentDef.GetName();
                        string curEngineTag = currentDef.Engine == "piper" ? "Piper" : "Silero";
                        var voiceNameBtn = new GUIButton(new RectTransform(new Vector2(0.72f, 1f), stepperGroup.RectTransform), $"{curVoiceName} [{curEngineTag}]", Alignment.Center, "GUIButton");
                        voiceNameBtn.TextColor = currentDef.Engine == "piper" ? new Color(140, 220, 255) : new Color(255, 205, 120);

                        var nextVoiceBtn = new GUIButton(new RectTransform(new Vector2(0.14f, 1f), stepperGroup.RectTransform), ">", Alignment.Center, "GUIButton");

                        var capturedAssign = assign;
                        int capturedIdx = curIdx;

                        Action<int> updateVoiceAction = delta =>
                        {
                            capturedIdx = (capturedIdx + delta) % voiceList.Count;
                            if (capturedIdx < 0) capturedIdx += voiceList.Count;
                            var newDef = voiceList[capturedIdx];
                            capturedAssign.VoiceId = newDef.Id;
                            capturedAssign.Engine = newDef.Engine;
                            TTSManager.BotVoices[botName] = capturedAssign;

                            voiceNameBtn.Text = $"{newDef.GetName()} [{(newDef.Engine == "piper" ? "Piper" : "Silero")}]";
                            voiceNameBtn.TextColor = newDef.Engine == "piper" ? new Color(140, 220, 255) : new Color(255, 205, 120);
                        };

                        prevVoiceBtn.OnClicked = (b, ud) => { updateVoiceAction(-1); return true; };
                        nextVoiceBtn.OnClicked = (b, ud) => { updateVoiceAction(1); return true; };
                        voiceNameBtn.OnClicked = (b, ud) => { updateVoiceAction(1); return true; };

                        // Quick Preview Button [ ▶ ]
                        var prevBtn = new GUIButton(new RectTransform(new Vector2(0.12f, 1f), rowLayout.RectTransform), "", Alignment.Center, "GUIButton");
                        prevBtn.ToolTip = TTSManager.GetLoc("Прослушать голос этого бота", "试听此AI船员的声音", "Preview this bot's voice");

                        if (hasIcons)
                        {
                            try
                            {
                                var playSprite = new Sprite(iconAtlasPath, new Rectangle(384, 0, 64, 64));
                                var playImg = new GUIImage(new RectTransform(new Vector2(0.55f, 0.55f), prevBtn.RectTransform, Anchor.Center), playSprite, scaleToFit: true)
                                {
                                    CanBeFocused = false
                                };
                                playImg.Color = new Color(255, 205, 120);
                                playImg.HoverColor = Color.White;
                                playImg.SelectedColor = Color.White;
                            }
                            catch { }
                        }
                        else
                        {
                            prevBtn.Text = ">";
                            prevBtn.TextColor = Color.Orange;
                        }

                        prevBtn.OnClicked = (b, ud) =>
                        {
                            var curV = voiceList.Find(v => v.Id == capturedAssign.VoiceId && v.Engine == capturedAssign.Engine) ?? voiceList[capturedIdx];
                            string sample = curV.Lang == "ru" 
                                ? $"{botName} на связи, указания приняты." 
                                : (curV.Lang == "zh" ? $"{botName} 收到，等待指令。" : $"{botName} here, standing by.");
                            TTSManager.PreviewVoice(sample, curV.Id, capturedAssign.Speed, curV.Engine);
                            return true;
                        };

                        // Reset this bot button
                        var resetSingleBtn = new GUIButton(new RectTransform(new Vector2(0.06f, 1f), rowLayout.RectTransform), "X", Alignment.Center, "GUIButton");
                        resetSingleBtn.TextColor = Color.Salmon;
                        resetSingleBtn.ToolTip = TTSManager.GetLoc("Удалить или сбросить настройки этого бота", "重置或删除该船员配置", "Reset or remove this bot's config");
                        resetSingleBtn.OnClicked = (b, ud) =>
                        {
                            TTSManager.BotVoices.Remove(botName);
                            showBotsTab();
                            return true;
                        };
                    }
                }

                // Manual Bot Add Bar at bottom of tab
                var manualAddRow = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.08f), layout.RectTransform), isHorizontal: true)
                {
                    RelativeSpacing = 0.02f
                };

                var manualInput = new GUITextBox(new RectTransform(new Vector2(0.70f, 1f), manualAddRow.RectTransform), "")
                {
                    ToolTip = TTSManager.GetLoc("Введите имя бота для добавления в каталог", "输入AI船员名字以添加自定义配置", "Enter bot name to register in catalog")
                };

                var addBtn = new GUIButton(new RectTransform(new Vector2(0.28f, 1f), manualAddRow.RectTransform), TTSManager.GetLoc("+ ДОБАВИТЬ БОТА", "+ 添加船员", "+ ADD BOT"), Alignment.Center, "GUIButton");
                addBtn.TextColor = Color.Turquoise;
                addBtn.OnClicked = (b, ud) =>
                {
                    string newName = manualInput.Text?.Trim();
                    if (!string.IsNullOrEmpty(newName) && !TTSManager.BotVoices.ContainsKey(newName))
                    {
                        TTSManager.BotVoices[newName] = new BotVoiceAssignment
                        {
                            BotName = newName,
                            VoiceId = "aidar",
                            Engine = "silero",
                            Speed = 0
                        };
                        manualInput.Text = "";
                        showBotsTab();
                    }
                    return true;
                };
            };

            // Tab 3: Audio & Levels
            Action showAudioTab = () =>
            {
                contentCard.ClearChildren();
                var layout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), contentCard.RectTransform, Anchor.Center)) { RelativeSpacing = 0.04f };

                new GUITextBlock(new RectTransform(new Vector2(1f, 0.08f), layout.RectTransform), TTSManager.GetLoc("ГРОМКОСТЬ И ЗВУК", "音量与音频设置", "VOLUME & AUDIO"), textAlignment: Alignment.CenterLeft)
                {
                    TextColor = Color.White
                };

                AddSlider(layout, TTSManager.GetLoc("ОБЩАЯ ГРОМКОСТЬ", "全局音量", "GLOBAL VOLUME"), draftGlobalVolume, 0f, 100f,
                    v => ((int)v) + "%",
                    v => draftGlobalVolume = (int)v,
                    TTSManager.GetLoc("Общая громкость мода. 100% = нормальная громкость Barotrauma.", "模组的主音量。100% 为默认标准。", "Master TTS volume. 100% = standard game audio level."));

                AddSlider(layout, TTSManager.GetLoc("УСИЛЕНИЕ (VOLUME BOOST)", "音量增益 (BOOST)", "VOLUME BOOST"), draftVolumeBoost, 100f, 500f,
                    v => ((int)v) + "%",
                    v => draftVolumeBoost = (int)v,
                    TTSManager.GetLoc("Усиление звука до 500% (полезно, если голоса кажутся слишком тихими).", "将语音增益放大最多至500%（适合潜艇环境嘈杂时）。", "Audio boost up to 500% (useful for loud submarine ambient)."),
                    isBoost: true);

                AddSlider(layout, TTSManager.GetLoc("БАЗОВАЯ СКОРОСТЬ РЕЧИ", "基础语速", "BASE SPEECH SPEED"), draftBaseRate, -10f, 10f,
                    v => (v > 0 ? "+" : "") + ((int)v).ToString(),
                    v => draftBaseRate = (int)v,
                    TTSManager.GetLoc("Базовая скорость речи для всех персонажей. 0 = стандарт.", "所有角色的全局基准语速。0 为正常值。", "Base speaking speed for all characters. 0 = default."));
            };

            // Tab 3: Radio & Suits
            Action showRadioTab = () =>
            {
                contentCard.ClearChildren();
                var layout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), contentCard.RectTransform, Anchor.Center)) { RelativeSpacing = 0.04f };

                new GUITextBlock(new RectTransform(new Vector2(1f, 0.08f), layout.RectTransform), TTSManager.GetLoc("РАЦИЯ И СКАФАНДРЫ", "对讲机与潜水服", "RADIO & SUITS"), textAlignment: Alignment.CenterLeft)
                {
                    TextColor = Color.White
                };

                var radioBox = new GUITickBox(new RectTransform(new Vector2(1f, 0.12f), layout.RectTransform), TTSManager.GetLoc("Эффект рации", "启用无线电对讲机滤波音效", "Radio Filter"))
                {
                    Selected = draftEnableRadioFilter,
                    ToolTip = TTSManager.GetLoc("Добавляет эффект рации и фоновые помехи к сообщениям по радио.", "应用无线电带通滤波及战术载波背景杂音。", "Applies radio bandpass filter and subtle static to radio chat.")
                };
                radioBox.OnSelected = (tb) => { draftEnableRadioFilter = tb.Selected; return true; };

                var muffleBox = new GUITickBox(new RectTransform(new Vector2(1f, 0.12f), layout.RectTransform), TTSManager.GetLoc("Глушить в скафандрах и под водой", "潜水服/全密闭头盔/水下发闷音效", "Muffle in Suits / Underwater"))
                {
                    Selected = draftEnableSuitMuffle,
                    ToolTip = TTSManager.GetLoc("Приглушает голос персонажа, если надет скафандр, закрытый шлем или он под водой.", "在水下、密闭潜水头盔内及跨舱室隔离门时呈现发闷的真实音效。", "Realistically muffles speech inside closed helmets, suits, or underwater.")
                };
                muffleBox.OnSelected = (tb) => { draftEnableSuitMuffle = tb.Selected; return true; };

                new GUITextBlock(new RectTransform(new Vector2(1f, 0.40f), layout.RectTransform),
                    TTSManager.GetLoc(
                        "Информация:\nМод поддерживает водолазные шлемы и костюмы из сторонних модов (Barotraumatic, Tidebreakers и др.). Отключите эти пункты, если хотите чистый звук без эффектов.",
                        "说明：\n本模组全面兼容各类潜水服模组。如果您需要无滤镜的原声清晰语音，可关闭以上两个开关。",
                        "Information:\nFully compatible with modded armor and diving suits. If you prefer completely dry, clean voices without atmospheric processing, disable both options above."
                    ),
                    textAlignment: Alignment.TopLeft, wrap: true)
                {
                    TextColor = Color.LightSlateGray
                };
            };

            // Tab 4: Server & Engine
            Action showServerTab = () =>
            {
                contentCard.ClearChildren();
                var layout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), contentCard.RectTransform, Anchor.Center)) { RelativeSpacing = 0.025f };

                new GUITextBlock(new RectTransform(new Vector2(1f, 0.08f), layout.RectTransform), TTSManager.GetLoc("СЕРВЕР И ДВИЖОК", "服务器与引擎设置", "SERVER & ENGINE"), textAlignment: Alignment.CenterLeft)
                {
                    TextColor = Color.White
                };

                var serverStartBtn = new GUIButton(new RectTransform(new Vector2(1f, 0.13f), layout.RectTransform), TTSManager.GetLoc("ЗАПУСТИТЬ ЛОКАЛЬНЫЙ СЕРВЕР", "启动本地TTS服务器", "START LOCAL SERVER"), Alignment.Center, "GUIButton");
                serverStartBtn.TextColor = Color.LightGreen;
                serverStartBtn.ToolTip = TTSManager.GetLoc("Запустить локальный сервер синтеза речи (start_server.bat). Откроется окно консоли.", "在当前计算机上启动本地TTS服务器（控制台窗口）。", "Launch local TTS speech server (start_server.bat) in a console window.");
                serverStartBtn.OnClicked = (b, ud) =>
                {
                    if (StartServerProcess())
                    {
                        serverStartBtn.Text = TTSManager.GetLoc("Запуск сервера...", "正在启动...", "Starting server...");
                        TTSManager.CheckServerStatusAsync();
                    }
                    return true;
                };

                new GUITextBlock(new RectTransform(new Vector2(1f, 0.05f), layout.RectTransform), TTSManager.GetLoc("ДВИЖОК СИНТЕЗА:", "语音合成引擎：", "TTS ENGINE:"), textAlignment: Alignment.CenterLeft)
                {
                    TextColor = Color.LightGray
                };

                var engineDrop = new GUIDropDown(new RectTransform(new Vector2(1f, 0.12f), layout.RectTransform), "");
                engineDrop.AddItem(TTSManager.GetLoc("Piper (Реалистично, на CPU)", "Piper (逼真自然, CPU运算)", "Piper (Realistic, CPU-based)"), "piper");
                engineDrop.AddItem(TTSManager.GetLoc("Silero (Быстро, классика)", "Silero (经典极速, 轻量)", "Silero (Fast & Classic)"), "silero");
                engineDrop.SelectItem(draftTTSEngine);
                engineDrop.OnSelected = (guiComponent, obj) =>
                {
                    if (obj is string eng)
                    {
                        draftTTSEngine = eng;
                    }
                    return true;
                };

                new GUITextBlock(new RectTransform(new Vector2(1f, 0.05f), layout.RectTransform), TTSManager.GetLoc("ЧАСТОТА ДИСКРЕТИЗАЦИИ:", "采样率清晰度：", "SAMPLE RATE:"), textAlignment: Alignment.CenterLeft)
                {
                    TextColor = Color.LightGray
                };

                var rateDrop = new GUIDropDown(new RectTransform(new Vector2(1f, 0.12f), layout.RectTransform), "");
                rateDrop.AddItem(TTSManager.GetLoc("24000 Hz (Оптимально)", "24000 Hz (推荐标准)", "24000 Hz (Optimal)"), 24000);
                rateDrop.AddItem(TTSManager.GetLoc("48000 Hz (Высокое качество)", "48000 Hz (高清晰度)", "48000 Hz (High Quality)"), 48000);
                rateDrop.AddItem(TTSManager.GetLoc("8000 Hz (Рация / Lo-Fi)", "8000 Hz (复古电台)", "8000 Hz (Radio / Lo-Fi)"), 8000);
                rateDrop.SelectItem(draftSampleRate);
                rateDrop.OnSelected = (guiComponent, obj) =>
                {
                    if (obj is int r)
                    {
                        draftSampleRate = r;
                    }
                    return true;
                };

                var pingBtn = new GUIButton(new RectTransform(new Vector2(1f, 0.12f), layout.RectTransform), TTSManager.GetLoc("Проверить статус сервера", "检测服务器状态", "Ping TTS Server"), Alignment.Center, "GUIButton");
                pingBtn.OnClicked = (b, ud) =>
                {
                    TTSManager.CheckServerStatusAsync();
                    return true;
                };

                new GUITextBlock(new RectTransform(new Vector2(1f, 0.22f), layout.RectTransform),
                    TTSManager.GetLoc(
                        "Защита от сбоев:\nЕсли Piper не может синтезировать фразу или недоступен, сервер автоматически синтезирует её через Silero и покажет уведомление в игре.",
                        "容灾保护：\n若Piper出现异常或无法合成，系统将自动使用Silero兜底并在游戏内发出提示。",
                        "Fail-safe protection:\nIf Piper encounters an error, the backend seamlessly synthesizes via Silero and displays an in-game notice."
                    ),
                    textAlignment: Alignment.TopLeft, wrap: true)
                {
                    TextColor = Color.LightSlateGray
                };
            };

            // Tab 5: Author & License (GPL-3.0)
            Action showLicenseTab = () =>
            {
                contentCard.ClearChildren();
                var layout = new GUILayoutGroup(new RectTransform(new Vector2(0.95f, 0.95f), contentCard.RectTransform, Anchor.Center)) { RelativeSpacing = 0.02f };

                new GUITextBlock(new RectTransform(new Vector2(1f, 0.08f), layout.RectTransform), TTSManager.GetLoc("АВТОР И ЛИЦЕНЗИЯ (GNU GPLv3)", "作者与开源协议 (GNU GPLv3)", "AUTHOR & LICENSE (GNU GPLv3)"), textAlignment: Alignment.CenterLeft)
                {
                    TextColor = Color.Gold
                };

                new GUITextBlock(new RectTransform(new Vector2(1f, 0.05f), layout.RectTransform), "BaroVoices TTS • Автор: VORON (VORON2272)", textAlignment: Alignment.CenterLeft)
                {
                    TextColor = Color.White
                };

                string licenseTerms = TTSManager.GetLoc(
                    "Данный мод является свободным ПО и распространяется на условиях лицензии GNU General Public License v3.0 (GPL-3.0).\n\n" +
                    "Обязательные требования лицензии GPL-3.0 при любых модификациях и форках:\n" +
                    "1. Сохранение авторства: Оригинальное имя автора (VORON) и данная страница лицензии НЕ подлежат удалению.\n" +
                    "2. Открытый исходный код: Любые производные версии и сборки обязаны распространяться исключительно с открытым исходным кодом под той же лицензией GNU GPLv3.\n" +
                    "3. Уведомление об изменениях: Все внесённые модификации кода должны быть задокументированы.\n\n" +
                    "Удаление информации об авторе или распространение закрытых сборок нарушает условия лицензии GNU GPLv3.",

                    "本模组为自由开源软件，基于 GNU General Public License v3.0 (GPL-3.0) 协议分发。\n\n" +
                    "所有分叉 (Fork) 与修改版本必须严格遵守以下 GPL-3.0 条款：\n" +
                    "1. 保留原作者署名：原作者 (VORON) 署名及本许可证页面严禁删除。\n" +
                    "2. 源码完全公开：任何衍生版本均必须以 GNU GPLv3 协议开源并提供全部源码。\n" +
                    "3. 标明修改：对原代码的所有改动必须明确记录。\n\n" +
                    "删除作者信息或闭源分发均属侵权违法行为。",

                    "This mod is free software distributed under the terms of the GNU General Public License v3.0 (GPL-3.0).\n\n" +
                    "Mandatory GPL-3.0 requirements for all forks, modifications, and modpacks:\n" +
                    "1. Author Attribution: Original author credit (VORON) and this license tab MUST NOT be removed.\n" +
                    "2. Open Source: Any derivative works must be distributed under the identical GNU GPLv3 license with full source code.\n" +
                    "3. Change Tracking: All code changes must be clearly documented.\n\n" +
                    "Removing author credits or closing the source code violates the GNU GPLv3 license."
                );

                new GUITextBlock(new RectTransform(new Vector2(1f, 0.58f), layout.RectTransform), licenseTerms, textAlignment: Alignment.TopLeft, wrap: true)
                {
                    TextColor = Color.LightSlateGray
                };

                var openLicenseBtn = new GUIButton(new RectTransform(new Vector2(1f, 0.14f), layout.RectTransform), TTSManager.GetLoc("ОТКРЫТЬ ФАЙЛ ЛИЦЕНЗИИ (LICENSE)", "打开模组许可证文件 (LICENSE)", "OPEN LICENSE FILE (LICENSE)"), Alignment.Center, "GUIButton");
                openLicenseBtn.TextColor = Color.Gold;
                openLicenseBtn.ToolTip = TTSManager.GetLoc("Открыть файл LICENSE из папки мода.", "在文本编辑器中打开模组文件夹内的LICENSE文件。", "Open LICENSE file from mod directory.");
                openLicenseBtn.OnClicked = (b, ud) =>
                {
                    try
                    {
                        string licensePath = GetModPath("LICENSE");
                        if (File.Exists(licensePath))
                        {
                            string fullPath = Path.GetFullPath(licensePath);
                            int p = (int)Environment.OSVersion.Platform;
                            bool isUnix = (p == 4) || (p == 6) || (p == 128);
                            if (isUnix)
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = "xdg-open",
                                    Arguments = $"\"{fullPath}\"",
                                    UseShellExecute = false
                                });
                            }
                            else
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = "notepad.exe",
                                    Arguments = $"\"{fullPath}\"",
                                    UseShellExecute = true
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        TTSManager.Log("[BaroVoices TTS] Failed to open license: " + ex.Message);
                    }
                    return true;
                };
            };

            renderActiveTab = () =>
            {
                switch (activeTab)
                {
                    case 0: showGameplayTab(); break;
                    case 1: showVoiceTab(); break;
                    case 2: showBotsTab(); break;
                    case 3: showAudioTab(); break;
                    case 4: showRadioTab(); break;
                    case 5: showServerTab(); break;
                    case 6: showLicenseTab(); break;
                    default: showGameplayTab(); break;
                }
            };

            // Custom Tab Icons creation helper
            void CreateTabBtn(int index, string tip, Rectangle srcRect, Action onSelect)
            {
                var tabBtn = new GUIButton(new RectTransform(new Vector2(1f / 7.5f, 1f), tabBar.RectTransform), "", Alignment.Center, "GUITabButton");
                tabBtn.ToolTip = tip;

                if (hasIcons)
                {
                    try
                    {
                        var sprite = new Sprite(iconAtlasPath, srcRect);
                        var iconImg = new GUIImage(new RectTransform(new Vector2(0.65f, 0.65f), tabBtn.RectTransform, Anchor.Center), sprite, scaleToFit: true);
                        iconImg.Color = new Color(180, 230, 220, 255);
                        iconImg.HoverColor = new Color(240, 255, 250, 255);
                        iconImg.SelectedColor = Color.White;
                    }
                    catch { }
                }

                tabBtn.OnClicked = (b, ud) =>
                {
                    activeTab = index;
                    for (int i = 0; i < tabButtons.Count; i++)
                    {
                        if (tabButtons[i] != null) tabButtons[i].Selected = (i == activeTab);
                    }
                    renderActiveTab();
                    return true;
                };

                tabButtons.Add(tabBtn);
            }

            tabBar.RelativeSpacing = 0.01f;

            CreateTabBtn(0, TTSManager.GetLoc("Основные настройки", "基础设置", "General Settings"), new Rectangle(0, 0, 64, 64), showGameplayTab);
            CreateTabBtn(1, TTSManager.GetLoc("Мой голос", "我的声音", "My Voice"), new Rectangle(64, 0, 64, 64), showVoiceTab);
            CreateTabBtn(2, TTSManager.GetLoc("Голоса ботов (Экипаж)", "AI船员音色目录", "Bot Voices (Crew)"), new Rectangle(448, 0, 64, 64), showBotsTab);
            CreateTabBtn(3, TTSManager.GetLoc("Громкость и звук", "音量与音频", "Volume & Audio"), new Rectangle(128, 0, 64, 64), showAudioTab);
            CreateTabBtn(4, TTSManager.GetLoc("Рация и скафандры", "对讲机与潜水服", "Radio & Suits"), new Rectangle(192, 0, 64, 64), showRadioTab);
            CreateTabBtn(5, TTSManager.GetLoc("Сервер и движок", "服务器与引擎", "Server & Engine"), new Rectangle(256, 0, 64, 64), showServerTab);
            CreateTabBtn(6, TTSManager.GetLoc("Автор и лицензия (GPLv3)", "作者与许可证 (GPLv3)", "Author & License (GPLv3)"), new Rectangle(320, 0, 64, 64), showLicenseTab);

            for (int i = 0; i < tabButtons.Count; i++)
            {
                if (tabButtons[i] != null) tabButtons[i].Selected = (i == activeTab);
            }

            // Bottom Action Bar: Cancel, Reset Defaults, Apply
            var bottomBar = new GUILayoutGroup(new RectTransform(new Vector2(1f, 0.08f), rightCol.RectTransform), isHorizontal: true)
            {
                Stretch = true,
                RelativeSpacing = 0.02f
            };

            var cancelBtn = new GUIButton(new RectTransform(new Vector2(0.32f, 1f), bottomBar.RectTransform), TTSManager.GetLoc("ОТМЕНА", "取消", "CANCEL"), Alignment.Center, "GUIButton");
            cancelBtn.TextColor = Color.Salmon;
            cancelBtn.OnClicked = (b, ud) =>
            {
                CloseMenu();
                return true;
            };

            var resetBtn = new GUIButton(new RectTransform(new Vector2(0.32f, 1f), bottomBar.RectTransform), TTSManager.GetLoc("ПО УМОЛЧАНИЮ", "恢复默认", "DEFAULTS"), Alignment.Center, "GUIButton");
            resetBtn.OnClicked = (b, ud) =>
            {
                draftGlobalVolume = 100;
                draftVolumeBoost = 100;
                draftBaseRate = 0;
                draftEnableUniqueVoices = true;
                draftEnableBotTTS = true;
                draftEnableVoiceQueue = true;
                draftEnableSuitMuffle = true;
                draftEnableRadioFilter = true;
                draftTTSEngine = "silero";
                draftSampleRate = 24000;
                draftMySpeed = 0;
                renderActiveTab();
                return true;
            };

            var applyBtn = new GUIButton(new RectTransform(new Vector2(0.32f, 1f), bottomBar.RectTransform), TTSManager.GetLoc("ПРИМЕНИТЬ", "应用设置", "APPLY"), Alignment.Center, "GUIButton");
            applyBtn.TextColor = Color.LightGreen;
            applyBtn.OnClicked = (b, ud) =>
            {
                try
                {
                    TTSManager.GlobalVolume = draftGlobalVolume;
                    TTSManager.VolumeBoost = draftVolumeBoost;
                    TTSManager.BaseRate = draftBaseRate;
                    TTSManager.EnableUniqueVoices = draftEnableUniqueVoices;
                    TTSManager.EnableBotTTS = draftEnableBotTTS;
                    TTSManager.EnableVoiceQueue = draftEnableVoiceQueue;
                    TTSManager.EnableSuitMuffle = draftEnableSuitMuffle;
                    TTSManager.EnableRadioFilter = draftEnableRadioFilter;
                    TTSManager.TTSEngine = draftTTSEngine;
                    TTSManager.SampleRate = draftSampleRate;
                    TTSManager.VoiceName = draftVoiceName;
                    TTSManager.MySpeed = draftMySpeed;
                    TTSManager.SaveSettings();
                    TTSManager.SaveBotVoices();

                    if (Character.Controlled != null)
                    {
                        string v = string.IsNullOrEmpty(TTSManager.VoiceName) ? "baya" : TTSManager.VoiceName;
                        string luaCmd = $"if SendMyVoiceSettings then SendMyVoiceSettings({TTSManager.MyPitch}, {TTSManager.MySpeed}, \"{v}\", \"{TTSManager.TTSEngine}\") end";
                        try { GameMain.LuaCs.Lua?.DoString(luaCmd); } catch { }
                    }

                    try
                    {
                        string appliedNotice = TTSManager.GetLoc(
                            "[BaroVoices TTS] Настройки успешно применены.",
                            "[BaroVoices TTS] 设置已保存并成功应用。",
                            "[BaroVoices TTS] Settings successfully applied."
                        );
                        GUI.AddMessage(appliedNotice, Color.LightGreen, 3.5f);
                    }
                    catch { }

                    CloseMenu();
                }
                catch (Exception ex)
                {
                    LuaCsLogger.LogError("[BaroVoices TTS] Apply Error: " + ex.Message);
                }
                return true;
            };

            // Initial render
            renderActiveTab();

            TTSManager.CheckServerStatusAsync();
            TTSManager.Log("[BaroVoices TTS] ToggleMenu: Vocal Terminal successfully initialized!");
        }
        catch (Exception ex)
        {
            LuaCsLogger.LogError("[BaroVoices TTS] ToggleMenu Error: " + ex.ToString());
            CloseMenu();
        }
    }
}
