using System;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace REFrameXIV.Services;


public sealed class HudTargetingService : IDisposable
{
    private readonly IFramework framework;
    private readonly ITargetManager targetManager;
    private readonly NativeContextMenuStyleService contextMenus;

    private ulong ownedMouseoverId;
    private IGameObject? ownedMouseoverActor;
    private long lastMouseoverTouchMs;
    private ulong stabilizingTargetId;
    private IGameObject? stabilizingTargetActor;
    private long stabilizeTargetUntilMs;
    private bool disposed;

    public HudTargetingService(IFramework framework, ITargetManager targetManager, NativeContextMenuStyleService contextMenus)
    {
        this.framework = framework;
        this.targetManager = targetManager;
        this.contextMenus = contextMenus;
        framework.Update += OnFrameworkUpdate;
    }

    public void TouchMouseover(IGameObject? actor)
    {
        if (disposed || actor is null || !actor.IsValid())
            return;

        try
        {
            targetManager.MouseOverTarget = actor;
            targetManager.MouseOverNameplateTarget = actor;
            ownedMouseoverActor = actor;
            ownedMouseoverId = actor.GameObjectId;
            lastMouseoverTouchMs = Environment.TickCount64;
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "RE:Frame could not assign the mouseover target.");
        }
    }


    public void RefreshOwnedMouseover()
    {
        if (disposed || ownedMouseoverId == 0 || ownedMouseoverActor is null)
            return;

        try
        {
            if (!ownedMouseoverActor.IsValid() || ownedMouseoverActor.GameObjectId != ownedMouseoverId)
            {
                ClearOwnedMouseover();
                return;
            }

            targetManager.MouseOverTarget = ownedMouseoverActor;
            targetManager.MouseOverNameplateTarget = ownedMouseoverActor;
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "RE:Frame could not refresh its mouseover target.");
        }
    }

    public void SetTarget(IGameObject? actor)
    {
        if (disposed || actor is null || !actor.IsValid())
            return;

        try
        {
            targetManager.Target = actor;
            stabilizingTargetActor = actor;
            stabilizingTargetId = actor.GameObjectId;
            stabilizeTargetUntilMs = Environment.TickCount64 + 180;
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "RE:Frame could not target {ActorName}.", actor.Name.ToString());
        }
    }


    public unsafe void OpenNativeContextMenu(IGameObject? actor)
    {
        if (disposed || actor is null || !actor.IsValid() || actor.Address == nint.Zero)
            return;

        try
        {
            contextMenus.MarkOpening(actor.GameObjectId);
            var agentHud = AgentHUD.Instance();
            if (agentHud == null)
            {
                Plugin.Log.Warning("RE:Frame could not open the native context menu because AgentHUD was unavailable.");
                return;
            }

            agentHud->OpenContextMenuFromTarget((GameObject*)actor.Address);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "RE:Frame could not open the native context menu for {ActorName}.", actor.Name.ToString());
        }
    }

    public void ClearOwnedMouseover()
    {
        if (ownedMouseoverId == 0)
            return;

        try
        {
            if (targetManager.MouseOverTarget?.GameObjectId == ownedMouseoverId)
                targetManager.MouseOverTarget = null;
            if (targetManager.MouseOverNameplateTarget?.GameObjectId == ownedMouseoverId)
                targetManager.MouseOverNameplateTarget = null;
        }
        catch (Exception ex)
        {
            Plugin.Log.Verbose(ex, "RE:Frame could not clear its mouseover target.");
        }
        finally
        {
            ownedMouseoverId = 0;
            ownedMouseoverActor = null;
            lastMouseoverTouchMs = 0;
        }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var now = Environment.TickCount64;


        if (stabilizingTargetId != 0)
        {
            if (now > stabilizeTargetUntilMs || stabilizingTargetActor is null ||
                !stabilizingTargetActor.IsValid() || stabilizingTargetActor.GameObjectId != stabilizingTargetId)
            {
                stabilizingTargetId = 0;
                stabilizingTargetActor = null;
                stabilizeTargetUntilMs = 0;
            }
            else
            {
                try
                {
                    if (targetManager.Target?.GameObjectId != stabilizingTargetId)
                        targetManager.Target = stabilizingTargetActor;
                }
                catch (Exception ex)
                {
                    Plugin.Log.Verbose(ex, "RE:Frame could not stabilize a HUD-selected target.");
                }
            }
        }


        if (ownedMouseoverId == 0)
            return;

        if (now - lastMouseoverTouchMs > 300)
            ClearOwnedMouseover();
        else
            RefreshOwnedMouseover();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        framework.Update -= OnFrameworkUpdate;
        stabilizingTargetId = 0;
        stabilizingTargetActor = null;
        stabilizeTargetUntilMs = 0;
        ClearOwnedMouseover();
    }
}
