if SERVER then return end

print("[TTS Mod] Initialized Client TTS script.")

local CustomVoices = {}

Networking.Receive("TTS_VOICE_SYNC", function(msg)
    local charId = msg.ReadUInt16()
    local pitch = msg.ReadInt16()
    local speed = msg.ReadInt16()
    local voiceName = msg.ReadString()
    CustomVoices[charId] = {pitch = pitch, speed = speed, voice = voiceName}
end)

_G.SendMyVoiceSettings = function(pitch, speed, voice)
    if not Character.Controlled then return end
    -- Set locally too
    CustomVoices[Character.Controlled.ID] = {pitch = pitch, speed = speed, voice = voice}
    
    if Game.Client then
        local msg = Networking.Start("TTS_VOICE_SYNC")
        msg.WriteUInt16(Character.Controlled.ID)
        msg.WriteInt16(pitch)
        msg.WriteInt16(speed)
        msg.WriteString(voice)
        Networking.Send(msg)
    end
end

local ttsManager = TTSManager
if not ttsManager then
    if LuaUserData and LuaUserData.CreateStatic then
        local success, result = pcall(LuaUserData.CreateStatic, "TTSManager")
        if success then ttsManager = result end
    elseif Reflection then
        local success, result = pcall(Reflection.GetType, "TTSManager")
        if success then ttsManager = result end
    end
end

if not ttsManager then
    print("[TTS Mod] Error: Could not load TTSManager C# class!")
    return
end

Hook.Patch("Barotrauma.ChatBox", "AddMessage", function(instance, ptable)
    local chatMsg = ptable["message"]
    if not chatMsg then return end
    
    local text = chatMsg.Text
    local character = chatMsg.Sender
    
    if not text or text == "" then return end
    
    -- Exclude lobby chat
    if not Game.RoundStarted then return end
    
    -- Exclude commands starting with / or !
    if string.sub(text, 1, 1) == "/" or string.sub(text, 1, 1) == "!" then return end
    
    local msgTypeStr = tostring(chatMsg.Type)
    if character and CustomVoices[character.ID] then
        if ttsManager.SpeakWithCustom then
            ttsManager.SpeakWithCustom(character, text, CustomVoices[character.ID].voice or "baya", CustomVoices[character.ID].speed, msgTypeStr)
        else
            ttsManager.Speak(character, text, msgTypeStr)
        end
    else
        ttsManager.Speak(character, text, msgTypeStr)
    end
end, Hook.HookMethodType.After)

print("[TTS Mod] Client TTS script loaded. SAPI Direct Playback enabled.")

local lastSentCharId = 0
local syncTimer = 0

Hook.Add("think", "TTSModThink", function()
    if ttsManager and ttsManager.Update then
        ttsManager.Update()
    end
    
    if Timer.GetTime() > syncTimer then
        syncTimer = Timer.GetTime() + 1.0
        if Character.Controlled then
            if Character.Controlled.ID ~= lastSentCharId then
                lastSentCharId = Character.Controlled.ID
                if ttsManager then
                    _G.SendMyVoiceSettings(ttsManager.MyPitch, ttsManager.MySpeed, ttsManager.VoiceName)
                end
            end
        else
            lastSentCharId = 0
        end
    end
end)

-- Request cache from server now that we are fully loaded
if Game.Client then
    local reqMsg = Networking.Start("TTS_REQUEST_SYNC")
    Networking.Send(reqMsg)
end
