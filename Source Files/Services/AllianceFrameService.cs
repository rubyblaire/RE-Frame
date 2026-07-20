using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using REFrameXIV.Localization;
using REFrameXIV.UI;

namespace REFrameXIV.Services;


public sealed class AllianceFrameService
{
    private const int NativeAllianceSlotCount = 20;
    private const int DisplayedAllianceMemberCount = 16;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(100);
    private static readonly string[] GroupPropertyNames =
    {
        "AllianceGroupIndex", "AlliancePartyIndex", "PartyIndex",
        "AllianceGroup", "AllianceParty", "AllianceId", "PartyId",
    };

    private readonly IPartyList partyList;
    private readonly IObjectTable objectTable;
    private readonly IPluginLog log;
    private readonly IPartyMember?[] cachedMembers = new IPartyMember?[DisplayedAllianceMemberCount];
    private readonly IGameObject?[] cachedActors = new IGameObject?[DisplayedAllianceMemberCount];
    private readonly int[] cachedGroupIds = { 1, 2 };
    private DateTime nextRefreshUtc = DateTime.MinValue;

    public AllianceFrameService(IPartyList partyList, IObjectTable objectTable, IPluginLog log)
    {
        this.partyList = partyList;
        this.objectTable = objectTable;
        this.log = log;
    }

    public bool IsAlliance
    {
        get
        {
            try
            {
                return partyList.IsAlliance;
            }
            catch
            {
                return false;
            }
        }
    }

    public string GetLocalGroupLabel()
        => Localizer.Text("hud.alliance.local_party", "YOUR PARTY");

    public string GetGroupLabel(int displayGroupIndex)
    {
        Refresh();
        return displayGroupIndex switch
        {
            0 => Localizer.Text("hud.alliance.allied_party_one", "ALLIED PARTY 1"),
            1 => Localizer.Text("hud.alliance.allied_party_two", "ALLIED PARTY 2"),
            _ => Localizer.Text("hud.alliance.allied_party", "ALLIED PARTY"),
        };
    }

    public IPartyMember? GetMember(int allianceGroupIndex, int slotIndex)
    {
        if (allianceGroupIndex is < 0 or > 1 || slotIndex is < 0 or > 7)
            return null;

        Refresh();
        return cachedMembers[allianceGroupIndex * 8 + slotIndex];
    }

    public IGameObject? GetActor(int allianceGroupIndex, int slotIndex)
    {
        if (allianceGroupIndex is < 0 or > 1 || slotIndex is < 0 or > 7)
            return null;

        Refresh();
        return cachedActors[allianceGroupIndex * 8 + slotIndex];
    }

    public int CountMembers(int allianceGroupIndex)
    {
        if (allianceGroupIndex is < 0 or > 1)
            return 0;

        Refresh();
        var count = 0;
        var offset = allianceGroupIndex * 8;
        for (var index = 0; index < 8; index++)
        {
            if (cachedMembers[offset + index] is not null)
                count++;
        }

        return count;
    }

    public void Invalidate() => nextRefreshUtc = DateTime.MinValue;

    private void Refresh()
    {
        var now = DateTime.UtcNow;
        if (now < nextRefreshUtc)
            return;

        nextRefreshUtc = now + RefreshInterval;
        Array.Clear(cachedMembers);
        Array.Clear(cachedActors);

        if (!IsAlliance)
            return;

        try
        {
            var ownContentIds = new HashSet<ulong>();
            var ownEntityIds = new HashSet<uint>();
            var ownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int? ownGroupId = TryReadAllianceGroup(partyList, out var partyListGroup) ? partyListGroup : null;

            for (var index = 0; index < 8; index++)
            {
                var address = partyList.GetPartyMemberAddress(index);
                if (address == 0)
                    continue;

                var member = partyList.CreatePartyMemberReference(address);
                if (member is null || !IsUsable(member))
                    continue;

                if (member.ContentId != 0)
                    ownContentIds.Add(member.ContentId);
                if (member.EntityId != 0)
                    ownEntityIds.Add(member.EntityId);
                var ownName = NormalizeMemberName(member);
                if (!string.IsNullOrEmpty(ownName))
                    ownNames.Add(ownName);
                if (ownGroupId is null && TryReadAllianceGroup(member, out var memberGroup))
                    ownGroupId = memberGroup;
            }

            var seenContentIds = new HashSet<ulong>();
            var seenEntityIds = new HashSet<uint>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var alliedMembers = new List<AllianceMemberSnapshot>(NativeAllianceSlotCount);
            for (var index = 0; index < NativeAllianceSlotCount; index++)
            {
                var address = partyList.GetAllianceMemberAddress(index);
                if (address == 0)
                    continue;

                var member = partyList.CreateAllianceMemberReference(address);
                if (member is null || !IsUsable(member))
                    continue;
                var normalizedName = NormalizeMemberName(member);
                if (member.ContentId != 0 && ownContentIds.Contains(member.ContentId))
                    continue;
                if (member.EntityId != 0 && ownEntityIds.Contains(member.EntityId))
                    continue;


                if (!string.IsNullOrEmpty(normalizedName) && ownNames.Contains(normalizedName))
                    continue;
                if (member.ContentId != 0 && !seenContentIds.Add(member.ContentId))
                    continue;
                if (member.EntityId != 0 && !seenEntityIds.Add(member.EntityId))
                    continue;
                if (!string.IsNullOrEmpty(normalizedName) && !seenNames.Add(normalizedName))
                    continue;

                var groupId = TryReadAllianceGroup(member, out var resolvedGroup) ? resolvedGroup : (int?)null;
                var actor = member.GameObject;
                if (actor is null && member.EntityId != 0)
                    actor = objectTable.FirstOrDefault(candidate => candidate.EntityId == member.EntityId);
                alliedMembers.Add(new AllianceMemberSnapshot(member, actor, groupId, index));
                if (alliedMembers.Count == DisplayedAllianceMemberCount)
                    break;
            }

            ResolveDisplayedGroups(ownGroupId, alliedMembers);
            PopulateGroups(alliedMembers);
        }
        catch (Exception ex)
        {
            log.Verbose(ex, "RE:Frame could not refresh the native alliance-member list.");
            Array.Clear(cachedMembers);
            Array.Clear(cachedActors);
        }
    }

    private void ResolveDisplayedGroups(int? ownGroupId, IReadOnlyList<AllianceMemberSnapshot> alliedMembers)
    {
        if (ownGroupId is >= 0 and <= 2)
        {
            var otherGroups = Enumerable.Range(0, 3).Where(group => group != ownGroupId.Value).ToArray();
            cachedGroupIds[0] = otherGroups[0];
            cachedGroupIds[1] = otherGroups[1];
            return;
        }

        var discovered = alliedMembers
            .Where(member => member.GroupId is >= 0 and <= 2)
            .Select(member => member.GroupId!.Value)
            .Distinct()
            .OrderBy(group => group)
            .Take(2)
            .ToArray();

        if (discovered.Length == 2)
        {
            cachedGroupIds[0] = discovered[0];
            cachedGroupIds[1] = discovered[1];
            return;
        }


        cachedGroupIds[0] = 1;
        cachedGroupIds[1] = 2;
    }

    private void PopulateGroups(IReadOnlyList<AllianceMemberSnapshot> alliedMembers)
    {
        var groups = new[]
        {
            new List<AllianceMemberSnapshot>(8),
            new List<AllianceMemberSnapshot>(8),
        };
        var unassigned = new List<AllianceMemberSnapshot>();

        foreach (var snapshot in alliedMembers)
        {
            var groupIndex = snapshot.GroupId == cachedGroupIds[0]
                ? 0
                : snapshot.GroupId == cachedGroupIds[1]
                    ? 1
                    : -1;

            if (groupIndex < 0 || groups[groupIndex].Count >= 8)
            {
                unassigned.Add(snapshot);
                continue;
            }

            groups[groupIndex].Add(snapshot);
        }

        foreach (var snapshot in unassigned)
        {
            var groupIndex = groups[0].Count < 8 ? 0 : groups[1].Count < 8 ? 1 : -1;
            if (groupIndex < 0)
                break;
            groups[groupIndex].Add(snapshot);
        }

        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            groups[groupIndex].Sort(static (left, right) =>
            {
                var leftJob = HudRenderer.ReadClassJobId(left.Member);
                if (leftJob == 0)
                    leftJob = HudRenderer.ReadClassJobId(left.Actor);
                var rightJob = HudRenderer.ReadClassJobId(right.Member);
                if (rightJob == 0)
                    rightJob = HudRenderer.ReadClassJobId(right.Actor);

                var roleComparison = HudRenderer.GetPartyRoleOrder(leftJob)
                    .CompareTo(HudRenderer.GetPartyRoleOrder(rightJob));
                return roleComparison != 0
                    ? roleComparison
                    : left.NativeIndex.CompareTo(right.NativeIndex);
            });

            for (var slotIndex = 0; slotIndex < groups[groupIndex].Count; slotIndex++)
                Store(groupIndex, slotIndex, groups[groupIndex][slotIndex]);
        }
    }

    private void Store(int groupIndex, int slotIndex, AllianceMemberSnapshot snapshot)
    {
        var destination = groupIndex * 8 + slotIndex;
        cachedMembers[destination] = snapshot.Member;
        cachedActors[destination] = snapshot.Actor;
    }

    private static bool TryReadAllianceGroup(object source, out int groupId)
    {
        groupId = -1;
        try
        {
            var type = source.GetType();
            foreach (var propertyName in GroupPropertyNames)
            {
                var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (property is null || property.GetIndexParameters().Length != 0)
                    continue;

                var value = property.GetValue(source);
                if (TryNormalizeAllianceGroup(value, propertyName, out groupId))
                    return true;
            }
        }
        catch
        {

        }

        return false;
    }

    private static bool TryNormalizeAllianceGroup(object? value, string propertyName, out int groupId)
    {
        groupId = -1;
        if (value is null)
            return false;

        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim().ToUpperInvariant() ?? string.Empty;
        if (text.EndsWith("A", StringComparison.Ordinal) || text.Contains("ALLIANCE A", StringComparison.Ordinal))
        {
            groupId = 0;
            return true;
        }
        if (text.EndsWith("B", StringComparison.Ordinal) || text.Contains("ALLIANCE B", StringComparison.Ordinal))
        {
            groupId = 1;
            return true;
        }
        if (text.EndsWith("C", StringComparison.Ordinal) || text.Contains("ALLIANCE C", StringComparison.Ordinal))
        {
            groupId = 2;
            return true;
        }

        try
        {
            var numeric = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            if (propertyName.Contains("Index", StringComparison.OrdinalIgnoreCase) && numeric is >= 0 and <= 2)
            {
                groupId = numeric;
                return true;
            }

            if (numeric is >= 1 and <= 3)
            {
                groupId = numeric - 1;
                return true;
            }

            if (numeric is >= 0 and <= 2)
            {
                groupId = numeric;
                return true;
            }
        }
        catch
        {

        }

        return false;
    }

    private static string NormalizeMemberName(IPartyMember member)
    {
        try
        {
            return member.Name.ToString().Trim().ToUpperInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool IsUsable(IPartyMember member)
    {
        if (member.ContentId != 0 || member.EntityId != 0)
            return true;

        try
        {
            return !string.IsNullOrWhiteSpace(member.Name.ToString());
        }
        catch
        {
            return false;
        }
    }

    private readonly record struct AllianceMemberSnapshot(IPartyMember Member, IGameObject? Actor, int? GroupId, int NativeIndex);
}
