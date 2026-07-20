using System;
using System.Collections.Generic;
using System.Reflection;
using Dalamud.Game.Chat;
using Dalamud.Plugin.Services;

namespace REFrameXIV.Services;


public sealed class PartyConnectionService : IDisposable
{
    private readonly IChatGui chatGui;
    private readonly IFramework framework;
    private readonly IPartyList partyList;
    private readonly IPluginLog log;
    private readonly HashSet<string> offlineNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly object sync = new();
    private DateTime nextCleanupUtc = DateTime.MinValue;
    private bool disposed;

    public PartyConnectionService(IChatGui chatGui, IFramework framework, IPartyList partyList, IPluginLog log)
    {
        this.chatGui = chatGui;
        this.framework = framework;
        this.partyList = partyList;
        this.log = log;

        chatGui.ChatMessage += OnChatMessage;
        framework.Update += OnFrameworkUpdate;
    }

    public bool IsOffline(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        lock (sync)
            return offlineNames.Contains(name.Trim());
    }

    private void OnChatMessage(IHandleableChatMessage message)
    {
        if (disposed)
            return;

        try
        {
            var text = message.OriginalMessage.ToString().Trim();
            if (string.IsNullOrWhiteSpace(text))
                text = message.Message.ToString().Trim();
            if (string.IsNullOrWhiteSpace(text))
                return;

            var wentOffline = text.Contains("has gone offline", StringComparison.OrdinalIgnoreCase)
                || text.Contains("has logged out", StringComparison.OrdinalIgnoreCase)
                || text.Contains("logged out.", StringComparison.OrdinalIgnoreCase)
                || text.Contains("has disconnected", StringComparison.OrdinalIgnoreCase)
                || text.Contains("has lost connection", StringComparison.OrdinalIgnoreCase)
                || text.Contains("has left the game", StringComparison.OrdinalIgnoreCase);
            var cameOnline = text.Contains("has logged in", StringComparison.OrdinalIgnoreCase)
                || text.Contains("has logged back in", StringComparison.OrdinalIgnoreCase)
                || text.Contains("has come online", StringComparison.OrdinalIgnoreCase)
                || text.Contains("is now online", StringComparison.OrdinalIgnoreCase)
                || text.Contains("is back online", StringComparison.OrdinalIgnoreCase)
                || text.Contains("has reconnected", StringComparison.OrdinalIgnoreCase)
                || text.Contains("has returned", StringComparison.OrdinalIgnoreCase)
                || text.Contains("has rejoined", StringComparison.OrdinalIgnoreCase);

            if (!wentOffline && !cameOnline)
                return;

            for (var i = 0; i < partyList.Length; i++)
            {
                var member = partyList[i];
                if (member is null)
                    continue;

                var name = member.Name.ToString().Trim();
                if (string.IsNullOrWhiteSpace(name) || !text.Contains(name, StringComparison.OrdinalIgnoreCase))
                    continue;

                lock (sync)
                {
                    if (wentOffline)
                        offlineNames.Add(name);
                    else
                        offlineNames.Remove(name);
                }
                break;
            }
        }
        catch (Exception ex)
        {
            log.Verbose(ex, "RE:Frame could not parse one party connection message.");
        }
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (disposed || DateTime.UtcNow < nextCleanupUtc)
            return;

        nextCleanupUtc = DateTime.UtcNow.AddSeconds(1);
        try
        {
            var currentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < partyList.Length; i++)
            {
                var member = partyList[i];
                if (member is null)
                    continue;

                var name = member.Name.ToString().Trim();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                currentNames.Add(name);


                if (!TryReadExplicitConnectionState(member, out var isOffline))
                    continue;

                lock (sync)
                {
                    if (isOffline)
                        offlineNames.Add(name);
                    else
                        offlineNames.Remove(name);
                }
            }

            lock (sync)
                offlineNames.RemoveWhere(name => !currentNames.Contains(name));
        }
        catch (Exception ex)
        {
            log.Verbose(ex, "RE:Frame could not refresh party connection state.");
        }
    }

    private static bool TryReadExplicitConnectionState(object member, out bool isOffline)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        var type = member.GetType();

        foreach (var propertyName in new[] { "IsOffline", "Offline", "IsDisconnected", "Disconnected" })
        {
            var property = type.GetProperty(propertyName, flags);
            if (property?.PropertyType != typeof(bool))
                continue;

            try
            {
                if (property.GetValue(member) is bool value)
                {
                    isOffline = value;
                    return true;
                }
            }
            catch
            {

            }
        }

        foreach (var propertyName in new[] { "IsOnline", "Online", "IsConnected", "Connected" })
        {
            var property = type.GetProperty(propertyName, flags);
            if (property?.PropertyType != typeof(bool))
                continue;

            try
            {
                if (property.GetValue(member) is bool value)
                {
                    isOffline = !value;
                    return true;
                }
            }
            catch
            {

            }
        }


        foreach (var propertyName in new[] { "OnlineStatus", "ConnectionStatus", "MemberStatus" })
        {
            var property = type.GetProperty(propertyName, flags);
            if (property is null)
                continue;

            try
            {
                var stateText = property.GetValue(member)?.ToString();
                if (string.IsNullOrWhiteSpace(stateText))
                    continue;

                if (stateText.Contains("offline", StringComparison.OrdinalIgnoreCase)
                    || stateText.Contains("disconnected", StringComparison.OrdinalIgnoreCase)
                    || stateText.Contains("loggedout", StringComparison.OrdinalIgnoreCase)
                    || stateText.Contains("logged out", StringComparison.OrdinalIgnoreCase))
                {
                    isOffline = true;
                    return true;
                }

                if (stateText.Contains("online", StringComparison.OrdinalIgnoreCase)
                    || stateText.Contains("connected", StringComparison.OrdinalIgnoreCase)
                    || stateText.Contains("loggedin", StringComparison.OrdinalIgnoreCase)
                    || stateText.Contains("logged in", StringComparison.OrdinalIgnoreCase))
                {
                    isOffline = false;
                    return true;
                }
            }
            catch
            {

            }
        }

        isOffline = false;
        return false;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        chatGui.ChatMessage -= OnChatMessage;
        framework.Update -= OnFrameworkUpdate;
        lock (sync)
        {
            offlineNames.Clear();
        }
    }
}
