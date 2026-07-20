using System;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using REFrameXIV.Models;

namespace REFrameXIV.Services;


public sealed unsafe class HotbarInputService
{
    private readonly HudTargetingService targeting;
    private readonly BarInputDiagnostics diagnostics;

    public HotbarInputService(HudTargetingService targeting, BarInputDiagnostics diagnostics)
    {
        this.targeting = targeting;
        this.diagnostics = diagnostics;
    }

    public bool ExecuteForMouseover(uint hotbarId, uint slotId, IGameObject actor)
    {
        targeting.TouchMouseover(actor);
        targeting.RefreshOwnedMouseover();
        diagnostics.RecordInputEvent($"Mouseover execution handler reached for Hotbar {hotbarId + 1}, Slot {slotId + 1}");
        return Execute(hotbarId, slotId);
    }

    public bool Execute(uint hotbarId, uint slotId)
    {
        var label = hotbarId == ReframeHotbarIds.PetBar
            ? $"Pet Bar, Slot {slotId + 1}"
            : $"Hotbar {hotbarId + 1}, Slot {slotId + 1}";
        diagnostics.RecordInputEvent($"Native execution handler reached for {label}");

        try
        {
            targeting.RefreshOwnedMouseover();
            var module = RaptureHotbarModule.Instance();
            if (module == null || !module->ModuleReady)
            {
                diagnostics.RecordExecution($"FAILED — {label}: RaptureHotbarModule was not ready");
                return false;
            }

            var slot = module->GetSlotById(hotbarId, slotId);
            if (slot == null)
            {
                diagnostics.RecordExecution($"FAILED — {label}: native slot pointer was null");
                return false;
            }

            if (slot->IsEmpty)
            {
                diagnostics.RecordExecution($"SAFE NO-OP — {label}: slot was empty");
                return false;
            }

            var result = module->ExecuteSlotById(hotbarId, slotId);
            diagnostics.RecordExecution($"EXECUTED — {label}: native result {result}");
            Plugin.Log.Debug("RE:Frame executed native hotbar {Hotbar}, slot {Slot}; native result {Result}.", hotbarId + 1, slotId + 1, result);
            return true;
        }
        catch (Exception ex)
        {
            diagnostics.RecordExecution($"EXCEPTION — {label}: {ex.GetType().Name}: {ex.Message}");
            Plugin.Log.Warning(ex, "RE:Frame could not execute hotbar {Hotbar}, slot {Slot}.", hotbarId + 1, slotId + 1);
            return false;
        }
    }
}
