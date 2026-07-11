using System;
using System.Collections.Generic;
using System.Linq;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.API.Features.Items;
using Exiled.Events.EventArgs.Player;
using MEC;
using PlayerRoles;
using PlayerRoles.PlayableScps.Scp106;
using UnityEngine;
using UserSettings.ServerSpecific;
using EventHUD.Hud;
using EventHUD.Medicine;
using EventHUD.Rpm;

namespace EventHUD.Scp
{
    /// <summary>
    /// SCP-106: коррозия при касании, 2-е касание = телепорт в карман + хил до 100 HP.
    /// SSS: keybind для телепорта в карман (КД 5 сек).
    /// </summary>
    public class Scp106Handler
    {
        private readonly Config _config;
        private static int _pocketKeybindId = 9020;
        private static bool _registered;

        // Отслеживание касаний 106
        private readonly Dictionary<string, DateTime> _lastTouchTime = new();
        private readonly Dictionary<string, int> _touchCount = new();

        // КД на телепорт в карман
        private readonly Dictionary<string, DateTime> _pocketCooldown = new();

        public Scp106Handler(Config config)
        {
            _config = config;
        }

        public void RegisterSss()
        {
            if (_registered) return;
            _registered = true;

            var keybind = new SSKeybindSetting(_pocketKeybindId, "Телепорт в карман (SCP-106)", UnityEngine.KeyCode.Alpha1);
            SssRoleSync.Scp106Settings.Add(keybind);

            ServerSpecificSettingsSync.ServerOnSettingValueReceived += OnSettingChanged;
        }

        public void UnregisterSss()
        {
            if (!_registered) return;
            _registered = false;
            ServerSpecificSettingsSync.ServerOnSettingValueReceived -= OnSettingChanged;
        }

        private void OnSettingChanged(ReferenceHub hub, ServerSpecificSettingBase setting)
        {
            if (setting.SettingId != _pocketKeybindId) return;
            if (!RpModuleManager.Instance.IsEnabled(RpModuleType.Medicine)) return;
            if (setting is not SSKeybindSetting keybind || !keybind.SyncIsPressed) return;

            var player = Player.Get(hub);
            if (player == null || !player.IsAlive) return;
            if (player.Role.Type != RoleTypeId.Scp106) return;

            // Уже в кармане — способность не работает
            if (player.CurrentRoom != null && player.CurrentRoom.Type == RoomType.Pocket)
            {
                HudNoticeService.Show(player, "<color=red>Вы уже в кармане измерения</color>", 1f);
                return;
            }

            // КД 5 сек
            if (_pocketCooldown.TryGetValue(player.UserId, out var lastUse))
            {
                if ((DateTime.UtcNow - lastUse).TotalSeconds < 5)
                {
                    HudNoticeService.Show(player, "<color=red>КД 5 секунд</color>", 1f);
                    return;
                }
            }
            _pocketCooldown[player.UserId] = DateTime.UtcNow;

            // Штатная анимация погружения (106 уходит под пол), затем телепорт в карман
            HudNoticeService.Show(player, "<color=#00FF00>Телепортация в карман...</color>", 2f);

            if (player.Role is Exiled.API.Features.Roles.Scp106Role scp106)
            {
                scp106.Vigor = 1f;   // полная энергия, чтобы погружение не оборвалось
                scp106.IsSubmerged = true; // запускает родную анимацию ухода под пол
            }

            Timing.CallDelayed(2f, () =>
            {
                if (player == null || !player.IsAlive) return;
                if (player.Role.Type != RoleTypeId.Scp106) return;

                var pocket = Room.Get(RoomType.Pocket);
                if (pocket == null) return;

                player.Position = pocket.Position + Vector3.up;

                if (player.Role is Exiled.API.Features.Roles.Scp106Role role106)
                    role106.IsSubmerged = false; // всплываем уже в кармане

                HudNoticeService.Show(player, "<color=#00FF00>Вы в кармане измерения</color>", 2f);
            });
        }

        /// <summary>
        /// Обработка касания SCP-106.
        /// 1-е касание: коррозия.
        /// 2-е касание: телепорт в карман + хил до 100 HP.
        /// </summary>
        public void OnHurting(HurtingEventArgs ev)
        {
            // SCP-106 неуязвим в карманном измерении и к урону кармана
            if (ev.Player != null && ev.Player.Role.Type == RoleTypeId.Scp106 &&
                (ev.DamageHandler.Type == Exiled.API.Enums.DamageType.PocketDimension ||
                 (ev.Player.CurrentRoom != null && ev.Player.CurrentRoom.Type == RoomType.Pocket)))
            {
                ev.IsAllowed = false;
                return;
            }

            if (ev.DamageHandler.Type != Exiled.API.Enums.DamageType.Scp106 &&
                ev.DamageHandler.Type != Exiled.API.Enums.DamageType.PocketDimension)
                return;

            if (ev.Player == null || !ev.Player.IsAlive) return;
            if (ev.Player.Role.Team == Team.SCPs) return;

            string uid = ev.Player.UserId;

            // Pocket Dimension damage = телепорт
            if (ev.DamageHandler.Type == Exiled.API.Enums.DamageType.PocketDimension)
            {
                // Хил до 100 HP
                ev.Player.Health = Math.Min(ev.Player.Health + 100f, ev.Player.MaxHealth);
                HudNoticeService.Show(ev.Player, "<color=#00FF00>Вы выжили в кармане! HP восстановлено.</color>", 3f);
                return;
            }

            // Касание 106
            DateTime now = DateTime.UtcNow;

            // Сброс счётчика если прошло > 5 секунд
            if (_lastTouchTime.TryGetValue(uid, out var lastTouch))
            {
                if ((now - lastTouch).TotalSeconds > 5)
                    _touchCount[uid] = 0;
            }
            _lastTouchTime[uid] = now;

            if (!_touchCount.ContainsKey(uid))
                _touchCount[uid] = 0;
            _touchCount[uid]++;

            if (_touchCount[uid] == 1)
            {
                // 1-е касание: коррозия
                ev.Amount = 0; // Не наносим урон
                InjuryApplier.ApplyCorrosion(ev.Player);
                HudNoticeService.Show(ev.Player, "<color=red>Коррозия! Коснитесь ещё раз для телепорта в карман.</color>", 3f);
            }
            else if (_touchCount[uid] >= 2)
            {
                // 2-е касание: телепорт в карман + хил
                ev.Amount = 0;
                _touchCount[uid] = 0;

                // Хил до 100 HP
                ev.Player.Health = Math.Min(ev.Player.Health + 100f, ev.Player.MaxHealth);

                // Телепорт в карман
                Timing.CallDelayed(0.5f, () =>
                {
                    if (ev.Player == null || !ev.Player.IsAlive) return;
                    ev.Player.Position = Room.Get(RoomType.Pocket).Position + Vector3.up;
                    HudNoticeService.Show(ev.Player, "<color=#FF0000>Вы телепортированы в карман измерения!</color>", 3f);
                });
            }
        }

        /// <summary>
        /// 106 выходит через правильный выход кармана — разрешаем и чистим эффекты.
        /// </summary>
        public void OnEscapingPocket(EscapingPocketDimensionEventArgs ev)
        {
            if (ev.Player == null || ev.Player.Role.Type != RoleTypeId.Scp106) return;

            ev.IsAllowed = true;
            ClearPocketEffects(ev.Player);
        }

        /// <summary>
        /// 106 пошёл в "неправильный" выход — вместо смерти телепорт в случайную комнату.
        /// Гарантирует 100% выход из кармана.
        /// </summary>
        public void OnFailingEscapePocket(FailingEscapePocketDimensionEventArgs ev)
        {
            if (ev.Player == null || ev.Player.Role.Type != RoleTypeId.Scp106) return;

            ev.IsAllowed = false;

            var player = ev.Player;
            var rooms = Room.List.Where(r => r.Type != RoomType.Pocket && r.Type != RoomType.Surface).ToList();
            if (rooms.Count == 0) return;
            var targetRoom = rooms[UnityEngine.Random.Range(0, rooms.Count)];

            Timing.CallDelayed(0.1f, () =>
            {
                if (player == null || !player.IsAlive) return;
                player.Position = targetRoom.Position + Vector3.up;
                ClearPocketEffects(player);
                HudNoticeService.Show(player, "<color=#00FF00>Вы вышли из кармана</color>", 2f);
            });
        }

        /// <summary>
        /// Снимает все "карманные" дебаффы, чтобы после выхода не было плохих эффектов.
        /// </summary>
        private void ClearPocketEffects(Player player)
        {
            Timing.CallDelayed(0.2f, () =>
            {
                if (player == null || !player.IsAlive) return;
                try
                {
                    player.DisableEffect<CustomPlayerEffects.PocketCorroding>();
                    player.DisableEffect<CustomPlayerEffects.Corroding>();
                    player.DisableEffect<CustomPlayerEffects.Traumatized>();
                }
                catch { }
            });
        }

        /// <summary>
        /// Когда 106 в кармане и идёт в коридор — телепорт в случайную комнату.
        /// </summary>
        public void OnChangingRoom(Player player)
        {
            if (player == null || !player.IsAlive) return;
            if (player.Role.Type != RoleTypeId.Scp106) return;

            // Проверяем, находится ли 106 в кармане
            if (player.CurrentRoom == null || player.CurrentRoom.Type != RoomType.Pocket)
                return;

            // Телепорт в случайную комнату
            var rooms = Room.List.Where(r => r.Type != RoomType.Pocket && r.Type != RoomType.Surface).ToList();
            if (rooms.Count == 0) return;

            var targetRoom = rooms[UnityEngine.Random.Range(0, rooms.Count)];
            Timing.CallDelayed(0.5f, () =>
            {
                if (player == null || !player.IsAlive) return;
                player.Position = targetRoom.Position + Vector3.up;
                HudNoticeService.Show(player, "<color=#00FF00>Вы вышли из кармана в случайной комнате</color>", 2f);
            });
        }
    }
}