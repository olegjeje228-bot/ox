using System;
using System.Collections.Generic;
using System.Linq;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.API.Features.Items;
using Exiled.API.Features.Pickups;
using Exiled.Events.EventArgs.Player;
using MEC;
using PlayerRoles;
using PlayerRoles.PlayableScps.Scp3114;
using UnityEngine;
using EventHUD.Hud;
using EventHUD.Rpm;

namespace EventHUD.Scp
{
    /// <summary>
    /// SCP-3114: бесконечная маскировка (пока не выстрелит или не снимет сам).
    /// Может использовать оружие, но поднять может только в маскировке.
    /// </summary>
    public class Scp3114Handler
    {
        private readonly Config _config;

        public Scp3114Handler(Config config)
        {
            _config = config;
        }

        /// <summary>
        /// Бесконечная маскировка для 3114.
        /// </summary>
        public void OnChangingRole(ChangingRoleEventArgs ev)
        {
            if (!RpModuleManager.Instance.IsEnabled(RpModuleType.Medicine))
                return;

            if (ev.Player == null) return;
            if (ev.NewRole != RoleTypeId.Scp3114) return;

            // При спавне 3114 даём бесконечную маскировку
            Timing.CallDelayed(1f, () =>
            {
                if (ev.Player == null || !ev.Player.IsAlive) return;
                if (ev.Player.Role.Type != RoleTypeId.Scp3114) return;

                // Включаем маскировку с максимальной длительностью
                try
                {
                    ev.Player.EnableEffect<CustomPlayerEffects.Invisible>(99999f);
                }
                catch { }
            });
        }

        /// <summary>
        /// 3114 может поднять оружие только в маскировке.
        /// </summary>
        public void OnItemAdded(ItemAddedEventArgs ev)
        {
            if (!RpModuleManager.Instance.IsEnabled(RpModuleType.Medicine))
                return;

            if (ev.Player == null || ev.Item == null) return;
            if (ev.Player.Role.Type != RoleTypeId.Scp3114) return;

            // Проверяем, в маскировке ли 3114 (по эффекту Invisible)
            bool isDisguised = ev.Player.TryGetEffect<CustomPlayerEffects.Invisible>(out _);

            if (!isDisguised)
            {
                // Не в маскировке — не даём поднять предмет
                Timing.CallDelayed(0.1f, () =>
                {
                    try
                    {
                        if (ev.Item != null)
                            ev.Item.Destroy();
                    }
                    catch { }
                });
                HudNoticeService.Show(ev.Player, "<color=red>Вы не можете поднять предмет без маскировки!</color>", 2f);
            }
        }

        /// <summary>
        /// 3114 может стрелять из оружия (для RP).
        /// </summary>
        public void OnShooting(ShootingEventArgs ev)
        {
            if (!RpModuleManager.Instance.IsEnabled(RpModuleType.Medicine))
                return;

            if (ev.Player == null) return;
            if (ev.Player.Role.Type != RoleTypeId.Scp3114) return;

            // 3114 может стрелять — разрешаем
            ev.IsAllowed = true;
        }
    }
}