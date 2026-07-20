using System;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace REFrameXIV.Services;

public enum NativeRaidTool
{
    Repair,
    Waymarks,
    Countdown,
    StrategyBoard,
}


public static unsafe class NativeRaidToolService
{
    private const AgentId FieldMarkerAgent = (AgentId)138;
    private const AgentId CountdownAgent = (AgentId)251;
    private const AgentId StrategyBoardListAgent = (AgentId)442;

    public static bool TryOpen(NativeRaidTool tool)
    {


        if (tool == NativeRaidTool.Repair)
            return NativeChatCommandService.TryExecute("/generalaction \"Repair\"");

        var agentId = tool switch
        {
            NativeRaidTool.Waymarks => FieldMarkerAgent,
            NativeRaidTool.Countdown => CountdownAgent,
            NativeRaidTool.StrategyBoard => StrategyBoardListAgent,
            _ => FieldMarkerAgent,
        };

        try
        {
            var module = AgentModule.Instance();
            if (module == null)
                return false;

            var agent = module->GetAgentByInternalId(agentId);
            if (agent == null)
                return false;


            agent->Show();
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "RE:Frame could not open native raid tool {Tool}.", tool);
            return false;
        }
    }
}
