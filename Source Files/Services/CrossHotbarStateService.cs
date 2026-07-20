using System;
using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace REFrameXIV.Services;

public readonly record struct CrossHotbarState(
    uint HotbarId,
    int SetNumber,
    bool LeftFocused,
    bool RightFocused,
    bool PetHotbarActive);


public sealed unsafe class CrossHotbarStateService
{
    private readonly IGameGui gameGui;
    private readonly IGameConfig gameConfig;
    private DateTime nextControllerProbeUtc = DateTime.MinValue;
    private bool controllerConnected;
    private bool xInputAvailable = true;

    public CrossHotbarStateService(IGameGui gameGui, IGameConfig gameConfig)
    {
        this.gameGui = gameGui;
        this.gameConfig = gameConfig;
    }


    public bool IsControllerUser
    {
        get
        {
            try
            {
                var padMode = gameConfig.UiConfig.TryGetBool("PadMode", out var enabled) && enabled;
                return padMode && HasConnectedController;
            }
            catch
            {
                return false;
            }
        }
    }

    public bool HasConnectedController
    {
        get
        {
            var now = DateTime.UtcNow;
            if (now < nextControllerProbeUtc)
                return controllerConnected;

            nextControllerProbeUtc = now.AddSeconds(1);
            controllerConnected = ProbeConnectedController();
            return controllerConnected;
        }
    }

    public bool TryGetState(out CrossHotbarState state)
    {
        state = default;

        if (!IsControllerUser)
            return false;

        var module = RaptureHotbarModule.Instance();
        if (module == null || !module->ModuleReady)
            return false;

        AtkUnitBase* addon;
        try
        {
            addon = gameGui.GetAddonByName<AtkUnitBase>("_ActionCross", 1);
        }
        catch
        {
            return false;
        }

        if (addon == null || !addon->IsReady)
            return false;


        var actionBar = (AddonActionBarBase*)addon;
        if (!actionBar->IsCrossHotbar)
            return false;

        var hotbarId = (uint)actionBar->RaptureHotbarId;
        if (hotbarId is < 10u or > 17u)
            return false;

        var flags = module->CrossHotbarFlags;
        var petHotbarActive = (flags & CrossHotbarFlags.PetHotbarActive) != 0 || actionBar->DisplayPetBar;
        state = new CrossHotbarState(
            hotbarId,
            (int)(hotbarId - 9u),
            (flags & CrossHotbarFlags.LeftSideFocus) != 0,
            (flags & CrossHotbarFlags.RightSideFocus) != 0,
            petHotbarActive);
        return true;
    }

    private bool ProbeConnectedController()
    {
        if (!xInputAvailable)
            return false;

        try
        {
            for (uint index = 0; index < 4; index++)
            {
                if (XInputGetState(index, out _) == 0)
                    return true;
            }
        }
        catch (DllNotFoundException)
        {
            xInputAvailable = false;
        }
        catch (EntryPointNotFoundException)
        {
            xInputAvailable = false;
        }

        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetState(uint userIndex, out XInputState state);
}
