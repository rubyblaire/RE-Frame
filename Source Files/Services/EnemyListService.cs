using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;

namespace REFrameXIV.Services;


public sealed class EnemyListService
{
    private readonly IObjectTable objectTable;
    private readonly ITargetManager targetManager;
    private readonly List<IBattleNpc> snapshot = new(16);
    private readonly HashSet<nint> seenAddresses = new();
    private readonly HashSet<ulong> seenGameObjectIds = new();
    private readonly HashSet<uint> seenEntityIds = new();
    private readonly HashSet<string> seenTrainingDummyNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<EnemySignature> seenSignatures = new(16);

    public EnemyListService(IObjectTable objectTable, ITargetManager targetManager)
    {
        this.objectTable = objectTable;
        this.targetManager = targetManager;
    }

    public IReadOnlyList<IBattleNpc> Snapshot(
        int maximum = 8,
        bool excludeCurrentTarget = false,
        bool excludeFocusTarget = false,
        bool engagedOnly = false)
    {
        snapshot.Clear();
        seenAddresses.Clear();
        seenGameObjectIds.Clear();
        seenEntityIds.Clear();
        seenTrainingDummyNames.Clear();
        seenSignatures.Clear();
        maximum = Math.Clamp(maximum, 1, 16);

        var localPlayer = objectTable.LocalPlayer;
        var currentTargetId = targetManager.Target?.GameObjectId ?? 0UL;
        var focusTargetId = targetManager.FocusTarget?.GameObjectId ?? 0UL;


        if (engagedOnly &&
            (localPlayer is null || (localPlayer.StatusFlags & StatusFlags.InCombat) == 0))
            return snapshot;

        foreach (var gameObject in objectTable)
        {
            if (gameObject is not IBattleNpc npc ||
                !npc.IsValid() ||
                npc.Address == nint.Zero ||
                npc.BattleNpcKind != BattleNpcSubKind.Combatant ||
                !npc.IsTargetable ||
                npc.IsDead ||
                npc.MaxHp == 0 ||
                npc.CurrentHp == 0)
                continue;

            if ((excludeCurrentTarget && currentTargetId != 0 && npc.GameObjectId == currentTargetId) ||
                (excludeFocusTarget && focusTargetId != 0 && npc.GameObjectId == focusTargetId))
                continue;


            if (engagedOnly && (npc.StatusFlags & StatusFlags.InCombat) == 0)
                continue;

            if (localPlayer is not null &&
                npc.GameObjectId != currentTargetId &&
                Vector3.DistanceSquared(localPlayer.Position, npc.Position) > 85f * 85f)
                continue;

            var name = NormalizeName(npc.Name.ToString());


            if (IsTrainingDummyName(name) && !seenTrainingDummyNames.Add(name))
                continue;

            var duplicate = seenAddresses.Contains(npc.Address) ||
                            (npc.GameObjectId != 0 && seenGameObjectIds.Contains(npc.GameObjectId)) ||
                            (npc.EntityId != 0 && seenEntityIds.Contains(npc.EntityId));

            if (!duplicate && !string.IsNullOrWhiteSpace(name))
            {
                foreach (var signature in seenSignatures)
                {
                    if (!string.Equals(signature.Name, name, StringComparison.OrdinalIgnoreCase) ||
                        signature.MaxHp != npc.MaxHp)
                        continue;

                    if (Vector3.DistanceSquared(signature.Position, npc.Position) <= 4f)
                    {
                        duplicate = true;
                        break;
                    }
                }
            }

            if (duplicate)
                continue;

            seenAddresses.Add(npc.Address);
            if (npc.GameObjectId != 0)
                seenGameObjectIds.Add(npc.GameObjectId);
            if (npc.EntityId != 0)
                seenEntityIds.Add(npc.EntityId);
            if (!string.IsNullOrWhiteSpace(name))
                seenSignatures.Add(new EnemySignature(name, npc.Position, npc.MaxHp));

            snapshot.Add(npc);
        }

        snapshot.Sort((left, right) =>
        {
            var leftTarget = left.GameObjectId == currentTargetId;
            var rightTarget = right.GameObjectId == currentTargetId;
            if (leftTarget != rightTarget)
                return leftTarget ? -1 : 1;

            if (localPlayer is null)
                return string.Compare(left.Name.ToString(), right.Name.ToString(), StringComparison.OrdinalIgnoreCase);

            var leftDistance = Vector3.DistanceSquared(localPlayer.Position, left.Position);
            var rightDistance = Vector3.DistanceSquared(localPlayer.Position, right.Position);
            return leftDistance.CompareTo(rightDistance);
        });

        if (snapshot.Count > maximum)
            snapshot.RemoveRange(maximum, snapshot.Count - maximum);

        return snapshot;
    }

    private static string NormalizeName(string name)
        => string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();

    private static bool IsTrainingDummyName(string name)
        => name.Contains("Striking Dummy", StringComparison.OrdinalIgnoreCase) ||
           name.Contains("Training Dummy", StringComparison.OrdinalIgnoreCase);

    private readonly record struct EnemySignature(string Name, Vector3 Position, ulong MaxHp);
}
