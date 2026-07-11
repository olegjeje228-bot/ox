using System;
using System.Collections.Generic;
using Exiled.API.Features;
using MEC;
using PlayerRoles;
using UserSettings.ServerSpecific;

namespace EventHUD.Hud
{
    /// <summary>
    /// Отправляет каждому игроку только те Server-Specific настройки,
    /// которые нужны его текущей роли:
    ///   люди    -> меню аптечки, рация, 914;
    ///   SCP-049 -> подбор предметов + меню;
    ///   SCP-106 -> телепорт в карман;
    ///   остальные SCP -> ничего.
    /// </summary>
    public static class SssRoleSync
    {
        public static readonly List<ServerSpecificSettingBase> HumanSettings  = new List<ServerSpecificSettingBase>();
        public static readonly List<ServerSpecificSettingBase> Scp049Settings = new List<ServerSpecificSettingBase>();
        public static readonly List<ServerSpecificSettingBase> Scp106Settings = new List<ServerSpecificSettingBase>();

        private static bool _initialized;

        /// <summary>Вызывать ПОСЛЕ того, как все хендлеры заполнили списки выше.</summary>
        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;

            // Общий пул серверу всё равно нужен — по нему принимаются значения по SettingId.
            var all = new List<ServerSpecificSettingBase>();
            all.AddRange(HumanSettings);
            foreach (var s in Scp049Settings) if (!all.Contains(s)) all.Add(s);
            foreach (var s in Scp106Settings) if (!all.Contains(s)) all.Add(s);
            ServerSpecificSettingsSync.DefinedSettings = all.ToArray();

            // Отключаем автоматическую рассылку ПОЛНОГО списка всем при заходе.
            ServerSpecificSettingsSync.SendOnJoinFilter = hub => false;
        }

        public static void Shutdown()
        {
            if (!_initialized) return;
            _initialized = false;
            ServerSpecificSettingsSync.SendOnJoinFilter = null;
            HumanSettings.Clear();
            Scp049Settings.Clear();
            Scp106Settings.Clear();
        }

        public static void SyncPlayer(Player player)
        {
            if (player == null || !player.IsConnected || player.ReferenceHub == null) return;

            ServerSpecificSettingBase[] set;
            switch (player.Role.Type)
            {
                case RoleTypeId.Scp049:
                    set = Scp049Settings.ToArray();
                    break;
                case RoleTypeId.Scp106:
                    set = Scp106Settings.ToArray();
                    break;
                default:
                    set = player.Role.Team == Team.SCPs
                        ? Array.Empty<ServerSpecificSettingBase>()
                        : HumanSettings.ToArray();
                    break;
            }

            ServerSpecificSettingsSync.SendToPlayer(player.ReferenceHub, set);
        }

        public static void OnChangingRole(Exiled.Events.EventArgs.Player.ChangingRoleEventArgs ev)
        {
            var player = ev.Player;
            // Роль применяется после события — шлём с небольшой задержкой.
            Timing.CallDelayed(0.6f, () => SyncPlayer(player));
        }

        public static void OnVerified(Exiled.Events.EventArgs.Player.VerifiedEventArgs ev)
        {
            var player = ev.Player;
            Timing.CallDelayed(1f, () => SyncPlayer(player));
        }
    }
}