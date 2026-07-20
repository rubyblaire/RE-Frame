using System;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace REFrameXIV.Services;


public static unsafe class NativeChatCommandService
{
    public static bool TryExecute(string? command)
    {
        var normalized = command?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        var uiModule = UIModule.Instance();
        if (uiModule == null)
            return false;

        using var message = new Utf8String(normalized);
        uiModule->ProcessChatBoxEntry(&message);
        return true;
    }
}
