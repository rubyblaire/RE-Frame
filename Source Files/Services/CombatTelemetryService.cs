using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;

namespace REFrameXIV.Services;


public sealed class CombatTelemetryService : IDisposable
{
    private readonly IFramework framework;
    private readonly IObjectTable objectTable;
    private readonly ITargetManager targetManager;
    private readonly ICondition condition;

    private readonly Queue<RateSample> damageIn = new();
    private readonly Queue<RateSample> healingIn = new();
    private readonly Queue<RateSample> targetPressure = new();

    private uint previousPlayerHp;
    private uint previousTargetHp;
    private uint playerMaxHp;
    private uint targetEntityId;
    private bool initialized;
    private DateTime encounterStartedUtc;

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(8);

    public CombatTelemetryService(IFramework framework, IObjectTable objectTable, ITargetManager targetManager, ICondition condition)
    {
        this.framework = framework;
        this.objectTable = objectTable;
        this.targetManager = targetManager;
        this.condition = condition;
        framework.Update += OnFrameworkUpdate;
    }

    public double DamageInPerSecond => Sum(damageIn) / Window.TotalSeconds;
    public double HealingReceivedPerSecond => Sum(healingIn) / Window.TotalSeconds;
    public double TargetPressurePerSecond => Sum(targetPressure) / Window.TotalSeconds;
    public uint PlayerMaxHp => playerMaxHp;
    public TimeSpan EncounterDuration => encounterStartedUtc == default ? TimeSpan.Zero : DateTime.UtcNow - encounterStartedUtc;

    public void Dispose() => framework.Update -= OnFrameworkUpdate;

    public void Reset()
    {
        damageIn.Clear();
        healingIn.Clear();
        targetPressure.Clear();
        encounterStartedUtc = default;
        previousTargetHp = 0;
        targetEntityId = 0;
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        var now = DateTime.UtcNow;
        Trim(damageIn, now);
        Trim(healingIn, now);
        Trim(targetPressure, now);

        var player = objectTable.LocalPlayer;
        if (player is null)
        {
            initialized = false;
            Reset();
            return;
        }

        playerMaxHp = player.MaxHp;
        if (!initialized)
        {
            previousPlayerHp = player.CurrentHp;
            initialized = true;
        }
        else
        {
            if (player.CurrentHp < previousPlayerHp)
                damageIn.Enqueue(new RateSample(now, previousPlayerHp - player.CurrentHp));
            else if (player.CurrentHp > previousPlayerHp)
                healingIn.Enqueue(new RateSample(now, player.CurrentHp - previousPlayerHp));

            previousPlayerHp = player.CurrentHp;
        }

        if (condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat] && encounterStartedUtc == default)
            encounterStartedUtc = now;
        else if (!condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InCombat] && encounterStartedUtc != default && damageIn.Count == 0 && healingIn.Count == 0)
            encounterStartedUtc = default;

        var target = targetManager.Target as IBattleChara;
        if (target is null || target.MaxHp == 0)
        {
            previousTargetHp = 0;
            targetEntityId = 0;
            return;
        }

        if (targetEntityId != target.EntityId)
        {
            targetEntityId = target.EntityId;
            previousTargetHp = target.CurrentHp;
            targetPressure.Clear();
            return;
        }

        if (previousTargetHp > 0 && target.CurrentHp < previousTargetHp)
            targetPressure.Enqueue(new RateSample(now, previousTargetHp - target.CurrentHp));

        previousTargetHp = target.CurrentHp;
    }

    private static double Sum(IEnumerable<RateSample> samples)
    {
        double total = 0;
        foreach (var sample in samples)
            total += sample.Amount;
        return total;
    }

    private static void Trim(Queue<RateSample> samples, DateTime now)
    {
        while (samples.Count > 0 && now - samples.Peek().TimeUtc > Window)
            samples.Dequeue();
    }

    private readonly record struct RateSample(DateTime TimeUtc, uint Amount);
}
