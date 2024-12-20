do
    -- NOTE: using "BlzSetAbilityTooltip" as hacky way to bridge the context between Preload and real game. 
    -- Preload can only execute native WC3 from blizzard.j & common.j, it can't access global variables from map trigger scripts.

    local UNIQUE_SAFETY_PREFIX = "{{UNIQUE_SAFETY_PREFIX}}"
    local PRELOAD_INPUT_FILENAME_PREFIX = "{{PRELOAD_INPUT_FILENAME_PREFIX}}"
    local PRELOAD_OUTPUT_FILENAME_PREFIX = "{{PRELOAD_OUTPUT_FILENAME_PREFIX}}"
    local PRELOAD_FILE_EXTENSION = "{{PRELOAD_FILE_EXTENSION}}"
    local PreloadFolderName = "{{PreloadFolderName}}"
    local _preloadBridge_AbilityCode = FourCC("{{_preloadBridge_AbilityCode}}")
    local OldToolTip = ""
    local InputCounter = 0

    local function SerializeValue(value)
        if type(value) == "table" then
            return SerializeTable(value)
        elseif type(value) == "string" then
            return "\"" .. value:gsub("\"", "\\\"") .. "\""
        elseif type(value) == "number" or type(value) == "boolean" then
            return tostring(value)
        else
            return nil
        end
    end

    local function SerializeTable(tbl, visited)
        visited = visited or {}
        if visited[tbl] then
            return nil -- Skip circular references silently
        end
        visited[tbl] = true

        local result = "{"
        for k, v in pairs(tbl) do
            local key = SerializeValue(k)
            local value = SerializeValue(v)
            if key and value then
                result = result .. "[" .. key .. "]=" .. value .. ","
            end
        end
        result = result .. "}"
        visited[tbl] = nil
        return result
    end

    local function OnLuaResponse(response)
        local fileName = PreloadFolderName .. "/" .. PRELOAD_OUTPUT_FILENAME_PREFIX .. (InputCounter - 1) .. PRELOAD_FILE_EXTENSION
        PreloadGenClear()
    
        if response then
            local maxChunkSize = 259
            local i = 1
            while i <= #response do
                local chunk = response:sub(i, i + maxChunkSize - 1)
                Preload(chunk)
                i = i + maxChunkSize
            end
        else
            Preload("nil")
        end
    
        PreloadGenStart()
        PreloadGenEnd(fileName)
    end

    local function ExecuteLuaFunctionBody(luaFunctionBody)
        local ok, result = pcall(function()
            return load(luaFunctionBody)()
        end)

        local response = nil
        if not ok then
            response = "Error: " .. tostring(result)
        else
            response = SerializeValue(result)
        end

        OnLuaResponse(response)
    end

    local function InjectAndExecuteLuaCode()
        local fileName = PreloadFolderName .. "/" .. PRELOAD_INPUT_FILENAME_PREFIX .. InputCounter .. PRELOAD_FILE_EXTENSION

        BlzSetAbilityTooltip(_preloadBridge_AbilityCode, OldToolTip, 0)
        Preloader(fileName)
        local luaFunctionBody = BlzGetAbilityTooltip(_preloadBridge_AbilityCode, 0)
        BlzSetAbilityTooltip(_preloadBridge_AbilityCode, OldToolTip, 0)

        if luaFunctionBody ~= OldToolTip then
            InputCounter = InputCounter + 1
            ExecuteLuaFunctionBody(luaFunctionBody)
        else
            return nil
        end
    end

    local function PreloadMonitor()
        InjectAndExecuteLuaCode()
    end

    local function startPreloadMonitor()
        OldToolTip = BlzGetAbilityTooltip(_preloadBridge_AbilityCode, 0)

        local timer = CreateTimer()
        TimerStart(timer, .1, true, PreloadMonitor)
        OnLuaResponse("Bridge Established")
    end

    _G[UNIQUE_SAFETY_PREFIX .. "main"] = _G["main"]
    _G.main = startPreloadMonitor    
end
