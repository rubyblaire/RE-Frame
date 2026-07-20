using FFXIVClientStructs.FFXIV.Client.UI;

namespace REFrameXIV.Services;


public static unsafe class NativeMainCommandService
{
    public static bool TryOpen(uint commandId)
    {
        var uiModule = UIModule.Instance();
        if (uiModule == null)
            return false;

        uiModule->ExecuteMainCommand(commandId);
        return true;
    }
}
