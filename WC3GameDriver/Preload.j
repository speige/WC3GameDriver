//NOTE: using "BlzSetAbilityTooltip" as hacky way to bridge the context between Preload and real game. Preload can only execute native WC3  from blizzard.j & common.j, it can't access global variables from map trigger scripts.

function PreloadFiles takes nothing returns nothing
//! beginusercode
BlzSetAbilityTooltip(FourCC("{{_preloadBridge_AbilityCode}}"), {{luaFunctionBody_AsString}}, 0)
//!endusercode
call PreloadEnd(0)
endfunction
