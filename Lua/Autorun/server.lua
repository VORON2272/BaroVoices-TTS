if CLIENT then return end

print("[TTS Mod] Initialized Server TTS script.")

local VoiceCache = {}

Networking.Receive("TTS_VOICE_SYNC", function(msg, senderClient)
    local charId = msg.ReadUInt16()
    local pitch = msg.ReadInt16()
    local speed = msg.ReadInt16()
    local voiceName = msg.ReadString()
    
    VoiceCache[charId] = {pitch = pitch, speed = speed, voiceName = voiceName}
    
    -- Relay to all other clients
    local outMsg = Networking.Start("TTS_VOICE_SYNC")
    outMsg.WriteUInt16(charId)
    outMsg.WriteInt16(pitch)
    outMsg.WriteInt16(speed)
    outMsg.WriteString(voiceName)
    
    for c in Client.ClientList do
        if c ~= senderClient then
            Networking.Send(outMsg, c.Connection)
        end
    end
end)

Hook.Add("clientConnected", "TTS_SendCache", function(connectedClient)
    -- Deprecated, moving to client-requested sync to avoid timing issues
end)

Networking.Receive("TTS_REQUEST_SYNC", function(msg, senderClient)
    for charId, data in pairs(VoiceCache) do
        local outMsg = Networking.Start("TTS_VOICE_SYNC")
        outMsg.WriteUInt16(charId)
        outMsg.WriteInt16(data.pitch)
        outMsg.WriteInt16(data.speed)
        outMsg.WriteString(data.voiceName)
        Networking.Send(outMsg, senderClient.Connection)
    end
end)

Hook.Add("roundEnd", "TTS_ClearCache", function()
    VoiceCache = {}
end)
