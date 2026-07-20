using System;
using System.Reflection;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace REFrameXIV.Services;


public sealed unsafe class PetBarStateService
{
    private static readonly string[] PetBarAddons =
    {
        "_PetHotbar", "PetHotbar", "_PetBar", "PetBar", "_BuddyAction", "BuddyAction",
    };

    private readonly IGameGui gameGui;
    private readonly IObjectTable objectTable;
    private DateTime nextRefreshUtc;
    private bool cached;

    public PetBarStateService(IGameGui gameGui, IObjectTable objectTable)
    {
        this.gameGui = gameGui;
        this.objectTable = objectTable;
    }

    public bool IsActive
    {
        get
        {
            if (DateTime.UtcNow < nextRefreshUtc)
                return cached;
            nextRefreshUtc = DateTime.UtcNow.AddMilliseconds(180);
            cached = ResolveActive();
            return cached;
        }
    }

    private bool ResolveActive()
    {
        foreach (var name in PetBarAddons)
        {
            var addon = gameGui.GetAddonByName<AtkUnitBase>(name, 1);
            if (addon != null && addon->IsReady && addon->IsVisible)
                return true;
        }

        var local = objectTable.LocalPlayer;
        if (local is null)
            return false;
        var localId = ReadNumericId(local, "GameObjectId", "EntityId", "ObjectId");
        if (localId == 0)
            return false;

        foreach (var gameObject in objectTable)
        {
            if (gameObject is null || ReferenceEquals(gameObject, local))
                continue;

            var typeName = gameObject.GetType().Name;
            if (!typeName.Contains("BattleNpc", StringComparison.OrdinalIgnoreCase) &&
                !typeName.Contains("Companion", StringComparison.OrdinalIgnoreCase) &&
                !typeName.Contains("Buddy", StringComparison.OrdinalIgnoreCase) &&
                !typeName.Contains("Pet", StringComparison.OrdinalIgnoreCase))
                continue;

            var ownerId = ReadNumericId(gameObject, "OwnerId", "OwnerEntityId", "OwnerObjectId", "OwnerGameObjectId");
            if (ownerId != 0 && ownerId == localId)
                return true;
        }

        return false;
    }

    private static ulong ReadNumericId(object source, params string[] propertyNames)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        foreach (var propertyName in propertyNames)
        {
            try
            {
                var value = source.GetType().GetProperty(propertyName, flags)?.GetValue(source);
                var converted = ConvertId(value);
                if (converted != 0)
                    return converted;
            }
            catch
            {

            }
        }
        return 0;
    }

    private static ulong ConvertId(object? value)
    {
        if (value is null)
            return 0;
        return value switch
        {
            byte v => v,
            ushort v => v,
            uint v => v,
            ulong v => v,
            sbyte v when v > 0 => (ulong)v,
            short v when v > 0 => (ulong)v,
            int v when v > 0 => (ulong)v,
            long v when v > 0 => (ulong)v,
            _ => ReadNestedId(value),
        };
    }

    private static ulong ReadNestedId(object value)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        foreach (var name in new[] { "Id", "Value", "ObjectId", "EntityId" })
        {
            try
            {
                var nested = value.GetType().GetProperty(name, flags)?.GetValue(value);
                if (nested is not null && !ReferenceEquals(nested, value))
                {
                    var converted = ConvertId(nested);
                    if (converted != 0)
                        return converted;
                }
            }
            catch
            {
            }
        }
        return 0;
    }
}
