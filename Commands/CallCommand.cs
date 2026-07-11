using System;
using System.Collections.Generic;
using System.ComponentModel;
using CommandSystem;
using Exiled.API.Features;
using Exiled.Permissions.Extensions;

namespace EventHUD.Commands
{
    [CommandHandler(typeof(ClientCommandHandler))]
    public class CallCommand : ICommand
    {
        private static readonly Dictionary<string, DateTime> _cooldowns = new();

        public string Command => "call";
        public string[] Aliases => Array.Empty<string>();
        public string Description => "Отправить запрос администрации";

        public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
        {
            var player = Player.Get(sender);
            if (player == null)
            {
                response = "Команда доступна только игрокам.";
                return false;
            }

            // Cooldown check
            if (_cooldowns.TryGetValue(player.UserId, out var lastUse))
            {
                if ((DateTime.UtcNow - lastUse).TotalSeconds < 5)
                {
                    response = "Подождите 5 секунд перед повторным использованием.";
                    return false;
                }
            }
            _cooldowns[player.UserId] = DateTime.UtcNow;

            string reason = arguments.Count > 0
                ? string.Join(" ", arguments).Trim()
                : string.Empty;

            // Reason max 60 chars
            if (reason.Length > 60)
                reason = reason.Substring(0, 60);

            string broadcastMsg;
            string consoleMsg;

            if (string.IsNullOrEmpty(reason))
            {
                broadcastMsg = $"<color=yellow>[{player.Id}] [{player.Nickname}] Отправляет .call!</color>";
                consoleMsg = $"[{player.Id}] [{player.Nickname}] Отправляет .call!";
            }
            else
            {
                broadcastMsg = $"<color=yellow>[{player.Id}] [{player.Nickname}] отправил .call, причина: {reason}</color>";
                consoleMsg = $"[{player.Id}] [{player.Nickname}] отправил .call, причина: {reason}";
            }

            // Broadcast to all players for 5 seconds
            Map.Broadcast(5, broadcastMsg);

            // Send to all admins in console
            foreach (var admin in Player.List)
            {
                if (admin.ReferenceHub.serverRoles.RemoteAdmin)
                {
                    admin.SendConsoleMessage(consoleMsg, "yellow");
                }
            }

            response = "Запрос отправлен администрации.";
            return true;
        }
    }
}