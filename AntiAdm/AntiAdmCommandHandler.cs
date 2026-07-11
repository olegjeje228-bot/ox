using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EventHUD.Hud;
using Exiled.API.Features;
using Exiled.API.Features.Pickups;
using Exiled.API.Features.Toys;
using Exiled.Events.EventArgs.Player;
using LabApi.Features.Enums;
using UnityEngine;

namespace EventHUD.AntiAdm
{
    /// <summary>
    /// Перехватывает RA-команды и блокирует запрещённые действия.
    /// 
    /// ID отказов:
    ///   AA-01: Лимит дамми превышен
    ///   AA-02: Нельзя связывать дамми
    ///   AA-03: changescale дамми > лимита
    ///   AA-04: Попытка забанить/кикнуть дамми
    ///   AA-05: forceclass спам (>3/сек)
    ///   AA-06: changescale > 30
    ///   AA-07: ccolor > 1000
    ///   AA-08: Патроны > 2000 за запрос
    ///   AA-09: Слишком много предметов у игрока
    ///   AA-10: Слишком много патронов у игрока
    ///   AA-11: Burst выдачи предметов/патронов админом
    ///   AA-12: Map editor лимиты (mp tg / mp cr / mp load)
    ///   AA-13: КД на команду
    ///   AA-14: Запрещённый предмет дамми (патроны/гранаты/018/фонарик)
    ///   AA-15: Лимит предметов дамми превышен (3)
    ///   AA-16: Лимит специальных предметов превышен (2)
    ///   AA-17: Лимит пачек патронов превышен (15)
    ///   AA-18: Лимит detonation_instant превышен (2 за минуту)
    ///   AA-19: Лимит forceclass дамми за минуту превышен (3)
    ///   AA-20: Дамми заблокированы (спам смертей)
    ///   AA-21: AdminToy лимит (макс 20)
    /// </summary>
    public class AntiAdmCommandHandler
    {
        private readonly Config _config;
        private readonly Dictionary<string, List<DateTime>> _forceClassHistory = new();
        private readonly List<DateTime> _dummyForceClassHistory = new();

        // Burst-история по админам
        private readonly Dictionary<string, List<DateTime>> _giveRequestsHistory = new();
        private readonly Dictionary<string, List<int>> _ammoBurstHistory = new();
        private readonly Dictionary<string, List<DateTime>> _itemsTwoMinuteHistory = new();

        // Map editor
        private readonly Dictionary<string, DateTime> _mpCrCooldown = new();
        private readonly Dictionary<string, int> _mpCrSchematicCount = new();
        private readonly Dictionary<string, int> _mpLoadUsage = new();

        // КД на любую команду
        private readonly Dictionary<string, DateTime> _lastCommandTime = new();

        // detonation_instant (2 per minute)
        private readonly List<DateTime> _detonationInstantTimes = new();

        // Дамми: спам смертей
        private readonly List<DateTime> _dummyDeathTimes = new();
        private DateTime _dummyBlockedUntil = DateTime.MinValue;

        public AntiAdmCommandHandler(Config config)
        {
            _config = config;
        }

        /// <summary>Сбрасывает все счётчики между раундами.</summary>
        public void Reset()
        {
            _forceClassHistory.Clear();
            _dummyForceClassHistory.Clear();
            _giveRequestsHistory.Clear();
            _ammoBurstHistory.Clear();
            _itemsTwoMinuteHistory.Clear();
            _mpCrCooldown.Clear();
            _mpCrSchematicCount.Clear();
            _mpLoadUsage.Clear();
            _lastCommandTime.Clear();
            _detonationInstantTimes.Clear();
            _dummyDeathTimes.Clear();
            _dummyBlockedUntil = DateTime.MinValue;
        }

        /// <summary>Парсит ID цели из RA-команды (поддерживает "5." с точкой).</summary>
        private static bool TryParseTargetId(string raw, out int id)
        {
            id = 0;
            if (string.IsNullOrEmpty(raw)) return false;
            // Remote Admin передаёт цель как "5." — обрезаем точку
            string cleaned = raw.TrimEnd('.');
            return int.TryParse(cleaned, out id);
        }

        /// <summary>Проверяет, является ли игрок дамми (через IsNPC, не по нику).</summary>
        private static bool IsDummy(Player player)
        {
            return player != null && player.IsNPC;
        }

        public void OnSendingValidCommand(SendingValidCommandEventArgs ev)
        {
            if (!_config.AntiAdmEnabled) return;
            if (ev.Type != CommandType.RemoteAdmin) return;
            if (string.IsNullOrEmpty(ev.Query)) return;

            string[] parts = ev.Query.Split(' ');
            if (parts.Length == 0) return;

            string cmd = parts[0].ToLowerInvariant();
            string[] args = parts.Length > 1 ? parts.Skip(1).ToArray() : Array.Empty<string>();
            string senderId = ev.Player?.UserId ?? "console";

            // ── AA-13: КД на любую команду ──
            if (_lastCommandTime.TryGetValue(senderId, out var lastCmd))
            {
                if ((DateTime.UtcNow - lastCmd).TotalSeconds < _config.AntiAdmCommandCooldown)
                {
                    Deny(ev, "AA-13");
                    return;
                }
            }
            _lastCommandTime[senderId] = DateTime.UtcNow;

            // ── AA-20: Дамми заблокированы ──
            bool dummyBlocked = DateTime.UtcNow < _dummyBlockedUntil;

            // ── Dummy spawn ──
            if (cmd == "dummy" && args.Length > 0 && args[0].ToLowerInvariant() == "spawn")
            {
                if (dummyBlocked)
                {
                    Deny(ev, "AA-20");
                    return;
                }
                int currentDummies = Player.List.Count(p => IsDummy(p));
                if (currentDummies >= _config.AntiAdmMaxDummies)
                {
                    Deny(ev, "AA-01");
                    return;
                }
            }

            // ── Dummy bind (нельзя связывать) ──
            if (cmd == "dummy" && args.Length > 0 && args[0].ToLowerInvariant() == "bind")
            {
                Deny(ev, "AA-02");
                return;
            }

            // ── disarm <id> дамми (нельзя связывать через команду) ──
            if ((cmd == "disarm" || cmd == "handcuff" || cmd == "cuff") && args.Length > 0)
            {
                if (TryParseTargetId(args[0], out int disarmId))
                {
                    var dt = Player.Get(disarmId);
                    if (IsDummy(dt))
                    {
                        Deny(ev, "AA-02");
                        return;
                    }
                }
            }

            // ── changescale ──
            if (cmd == "changescale" || cmd == "scale")
            {
                if (TryParseScaleArgs(args, out float maxVal, out bool isDummy))
                {
                    float limit = isDummy ? _config.AntiAdmDummyMaxScale : _config.AntiAdmMaxScale;
                    if (maxVal > limit)
                    {
                        Deny(ev, isDummy ? "AA-03" : "AA-06");
                        return;
                    }
                }
            }

            // ── ban / kick дамми ──
            if ((cmd == "ban" || cmd == "kick" || cmd == "oban") && args.Length > 0)
            {
                if (TryParseTargetId(args[0], out int targetId))
                {
                    var target = Player.Get(targetId);
                    if (IsDummy(target))
                    {
                        Deny(ev, "AA-04");
                        return;
                    }
                }
            }

            // ── forceclass ──
            if (cmd == "forceclass" || cmd == "fc")
            {
                // AA-05: forceclass спам (>3/сек) — на любую цель
                CleanForceClassHistory(senderId);
                _forceClassHistory[senderId].Add(DateTime.UtcNow);
                if (_forceClassHistory[senderId].Count > _config.AntiAdmMaxForceClassPerSecond)
                {
                    Deny(ev, "AA-05");
                    return;
                }

                // AA-19: forceclass дамми — максимум 3 за минуту
                if (args.Length > 0 && TryParseTargetId(args[0], out int fcTargetId))
                {
                    var fcTarget = Player.Get(fcTargetId);
                    if (IsDummy(fcTarget))
                    {
                        CleanDummyForceClassHistory();
                        _dummyForceClassHistory.Add(DateTime.UtcNow);
                        if (_dummyForceClassHistory.Count > _config.AntiAdmMaxDummyForceClassPerMinute)
                        {
                            Deny(ev, "AA-19");
                            return;
                        }
                    }
                }
            }

            // ── ccolor ──
            if (cmd == "ccolor" || cmd == "color")
            {
                if (TryParseColorArgs(args, out float maxChannel))
                {
                    if (maxChannel > _config.AntiAdmMaxColor)
                    {
                        Deny(ev, "AA-07");
                        return;
                    }
                }
            }

            // ── give / giveammo / ga ──
            if ((cmd == "give" || cmd == "giveammo" || cmd == "ga") && args.Length >= 2)
            {
                // Определяем целевого игрока
                Player target = null;
                if (TryParseTargetId(args[0], out int targetId))
                {
                    target = Player.Get(targetId);
                }

                bool isDummyTarget = IsDummy(target);

                // Определяем тип предмета (args[1] — ID предмета или название)
                int itemId = 0;
                if (int.TryParse(args[1], out itemId))
                {
                    ItemType itemType = (ItemType)itemId;

                    // AA-14: Запрещённые предметы дамми
                    if (isDummyTarget)
                    {
                        if (IsAmmoItem(itemType) ||
                            IsGrenadeItem(itemType) ||
                            itemType == ItemType.SCP018 ||
                            itemType == ItemType.Flashlight)
                        {
                            Deny(ev, "AA-14");
                            return;
                        }

                        // AA-15: Лимит предметов дамми (3)
                        if (target != null)
                        {
                            int dummyItems = CountInventoryItems(target);
                            if (dummyItems >= _config.AntiAdmMaxDummyItems)
                            {
                                Deny(ev, "AA-15");
                                return;
                            }
                        }
                    }

                    // AA-16: Специальные предметы — максимум 2
                    if (IsSpecialItem(itemType))
                    {
                        if (target != null)
                        {
                            int specialCount = CountSpecialItems(target);
                            if (specialCount >= _config.AntiAdmMaxSpecialItems)
                            {
                                Deny(ev, "AA-16");
                                return;
                            }
                        }
                    }

                    // AA-17: Пачки патронов — максимум 15
                    if (IsAmmoPackItem(itemType))
                    {
                        if (target != null)
                        {
                            int ammoPackCount = CountAmmoPacks(target);
                            if (ammoPackCount >= _config.AntiAdmMaxAmmoPacks)
                            {
                                Deny(ev, "AA-17");
                                return;
                            }
                        }
                    }
                }

                // AA-08: лимит за один запрос
                if (TryParseAmmoAmount(args, out int amount))
                {
                    if (amount > _config.AntiAdmMaxAmmo)
                    {
                        Deny(ev, "AA-08");
                        return;
                    }

                    // AA-10: суммарно патронов у целевого игрока (через player.Ammo)
                    if (target != null && GetTotalAmmo(target) + amount > _config.AntiAdmMaxTotalAmmo)
                    {
                        Deny(ev, "AA-10");
                        return;
                    }

                    // AA-11: burst патронов админом
                    if (GetAmmoBurst(senderId, amount) > _config.AntiAdmMaxAmmoBurst)
                    {
                        Deny(ev, "AA-11");
                        return;
                    }
                }

                // AA-09: лимит предметов у целевого игрока
                if (target != null)
                {
                    int itemCount = CountInventoryItems(target);
                    if (itemCount >= _config.AntiAdmMaxInventoryItems)
                    {
                        Deny(ev, "AA-09");
                        return;
                    }
                    if (itemCount + CountAmmoItems(target) >= _config.AntiAdmMaxTotalItems)
                    {
                        Deny(ev, "AA-09");
                        return;
                    }
                }

                // AA-11: burst запросов give/ga
                if (GetGiveRequestBurst(senderId) > _config.AntiAdmMaxGiveRequestsBurst)
                {
                    Deny(ev, "AA-11");
                    return;
                }

                // AA-09: 25 предметов за 2 минуты
                if (GetItemsTwoMinuteCount(senderId) >= _config.AntiAdmMaxItemsPerTwoMinutes)
                {
                    Deny(ev, "AA-09");
                    return;
                }

                RecordGive(senderId, amount);
            }

            // ── AA-18: server_event detonation_instant — максимум 2 за минуту ──
            if (cmd == "server_event" && args.Length > 0)
            {
                string subCmd = args[0].ToLowerInvariant();
                if (subCmd == "detonation_instant")
                {
                    var cutoff = DateTime.UtcNow.AddMinutes(-1);
                    _detonationInstantTimes.RemoveAll(t => t < cutoff);
                    if (_detonationInstantTimes.Count >= _config.AntiAdmMaxDetonationInstant)
                    {
                        Deny(ev, "AA-18");
                        return;
                    }
                    _detonationInstantTimes.Add(DateTime.UtcNow);
                }
            }

            // ── Map Editor: mp tg ──
            if (cmd == "mp" && args.Length >= 1 && args[0].ToLowerInvariant() == "tg")
            {
                string steamId = ev.Player?.UserId;
                if (string.IsNullOrEmpty(steamId) || steamId != _config.AntiAdmMpTgAllowedSteamId)
                {
                    Deny(ev, "AA-12");
                    return;
                }
            }

            // ── Map Editor: mp cr ──
            if (cmd == "mp" && args.Length >= 1 && args[0].ToLowerInvariant() == "cr")
            {
                if (_mpCrCooldown.TryGetValue(senderId, out var lastUse))
                {
                    if ((DateTime.UtcNow - lastUse).TotalSeconds < _config.AntiAdmMpCrCooldownSeconds)
                    {
                        Deny(ev, "AA-12");
                        return;
                    }
                }

                int current = _mpCrSchematicCount.GetValueOrDefault(senderId);
                bool isLarge = args.Any(a => a.ToLowerInvariant().Contains("large") || a.ToLowerInvariant().Contains("big"));
                int max = isLarge ? _config.AntiAdmMpCrMaxLargeSchematics : _config.AntiAdmMpCrMaxSchematics;

                if (current >= max)
                {
                    Deny(ev, "AA-12");
                    return;
                }

                _mpCrCooldown[senderId] = DateTime.UtcNow;
                _mpCrSchematicCount[senderId] = current + 1;
            }

            // ── Map Editor: mp load ──
            if (cmd == "mp" && args.Length >= 1 && args[0].ToLowerInvariant() == "load")
            {
                int used = _mpLoadUsage.GetValueOrDefault(senderId);
                if (used >= _config.AntiAdmMpLoadMaxUses)
                {
                    Deny(ev, "AA-12");
                    return;
                }
                _mpLoadUsage[senderId] = used + 1;
            }

            // ── AA-21: AdminToy лимит (макс 20) ──
            if ((cmd == "mp" && args.Length >= 1 && (args[0].ToLowerInvariant() == "cr" || args[0].ToLowerInvariant() == "load")) ||
                cmd == "admintoy")
            {
                int currentToys = 0;
                try
                {
                    currentToys = AdminToy.List.Count();
                }
                catch { }

                if (currentToys >= _config.AntiAdmMaxAdminToys)
                {
                    Deny(ev, "AA-21");
                    return;
                }
            }
        }

        /// <summary>
        /// Блокирует связывание (наручники) дамми в игре напрямую.
        /// AA-02.
        /// </summary>
        public void OnHandcuffing(HandcuffingEventArgs ev)
        {
            if (!_config.AntiAdmEnabled) return;
            if (ev.Target == null) return;

            if (IsDummy(ev.Target))
            {
                ev.IsAllowed = false;
                HudNoticeService.Show(ev.Player, "<color=red>Отказ [AA-02]</color>", 2f);
            }
        }

        /// <summary>
        /// Вызывать при смерти дамми для отслеживания спама.
        /// </summary>
        public void OnDummyDeath()
        {
            _dummyDeathTimes.Add(DateTime.UtcNow);
            CleanDummyDeathHistory();

            if (_dummyDeathTimes.Count > _config.AntiAdmDummyDeathLimitPerMinute)
            {
                _dummyBlockedUntil = DateTime.UtcNow.AddSeconds(_config.AntiAdmDummyDeathBlockDuration);
                Log.Warn($"[AntiAdm] Дамми заблокированы на {_config.AntiAdmDummyDeathBlockDuration}с (спам смертей: {_dummyDeathTimes.Count}/мин)");
                _dummyDeathTimes.Clear();
            }
        }

        public bool IsDummyBlocked => DateTime.UtcNow < _dummyBlockedUntil;

        private void CleanDummyDeathHistory()
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-1);
            _dummyDeathTimes.RemoveAll(t => t < cutoff);
        }

        private void Deny(SendingValidCommandEventArgs ev, string id)
        {
            ev.IsAllowed = false;
            ev.Response = $"<color=red>Отказ [{id}]</color>";
        }

        private void CleanForceClassHistory(string senderId)
        {
            if (!_forceClassHistory.TryGetValue(senderId, out var list))
            {
                _forceClassHistory[senderId] = new List<DateTime>();
                return;
            }
            var cutoff = DateTime.UtcNow.AddSeconds(-1);
            list.RemoveAll(t => t < cutoff);
        }

        private void CleanDummyForceClassHistory()
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-1);
            _dummyForceClassHistory.RemoveAll(t => t < cutoff);
        }

        // ── Helpers для give/ammo ──

        private int CountInventoryItems(Player player)
        {
            int count = 0;
            foreach (var item in player.Items)
            {
                if (item != null && !IsAmmoItem(item.Type))
                    count++;
            }
            return count;
        }

        private int CountAmmoItems(Player player)
        {
            // Патроны в SCP:SL хранятся отдельно от Items — считаем через player.Ammo
            int count = 0;
            foreach (var kv in player.Ammo)
            {
                count += kv.Value;
            }
            return count;
        }

        private int GetTotalAmmo(Player player)
        {
            // Патроны в SCP:SL хранятся в player.Ammo, не в Items
            int total = 0;
            foreach (var kv in player.Ammo)
            {
                total += kv.Value;
            }
            return total;
        }

        private bool IsAmmoItem(ItemType type)
        {
            return type == ItemType.Ammo556x45 ||
                   type == ItemType.Ammo762x39 ||
                   type == ItemType.Ammo9x19 ||
                   type == ItemType.Ammo12gauge ||
                   type == ItemType.Ammo44cal;
        }

        private bool IsGrenadeItem(ItemType type)
        {
            return type == ItemType.GrenadeHE ||
                   type == ItemType.GrenadeFlash ||
                   type == ItemType.SCP2176;
        }

        // Специальные предметы — используем ItemType enum, не магические числа
        private bool IsSpecialItem(ItemType type)
        {
            return type == ItemType.MicroHID ||
                   type == ItemType.GunShotgun ||
                   type == ItemType.GunCrossvec ||
                   type == ItemType.GunFRMG0 ||
                   type == ItemType.KeycardO5 ||
                   type == ItemType.GrenadeHE ||
                   type == ItemType.GrenadeFlash ||
                   type == ItemType.SCP2176;
        }

        private int CountSpecialItems(Player player)
        {
            int count = 0;
            foreach (var item in player.Items)
            {
                if (item == null) continue;
                if (IsSpecialItem(item.Type))
                    count++;
            }
            return count;
        }

        // Пачки патронов (пикапы) — используем ItemType
        private bool IsAmmoPackItem(ItemType type)
        {
            return type == ItemType.Ammo556x45 ||
                   type == ItemType.Ammo762x39 ||
                   type == ItemType.Ammo9x19 ||
                   type == ItemType.Ammo12gauge ||
                   type == ItemType.Ammo44cal;
        }

        private int CountAmmoPacks(Player player)
        {
            int count = 0;
            foreach (var item in player.Items)
            {
                if (item == null) continue;
                if (IsAmmoPackItem(item.Type))
                    count++;
            }
            return count;
        }

        // ── Burst helpers ──

        private int GetGiveRequestBurst(string senderId)
        {
            if (!_giveRequestsHistory.TryGetValue(senderId, out var list))
                return 0;
            var cutoff = DateTime.UtcNow.AddSeconds(-5);
            list.RemoveAll(t => t < cutoff);
            return list.Count;
        }

        private int GetAmmoBurst(string senderId, int amount)
        {
            if (!_ammoBurstHistory.TryGetValue(senderId, out var list))
                list = new List<int>();
            int sum = list.Sum();
            return sum + amount;
        }

        private int GetItemsTwoMinuteCount(string senderId)
        {
            if (!_itemsTwoMinuteHistory.TryGetValue(senderId, out var list))
                return 0;
            var cutoff = DateTime.UtcNow.AddMinutes(-2);
            list.RemoveAll(t => t < cutoff);
            return list.Count;
        }

        private void RecordGive(string senderId, int amount)
        {
            if (!_giveRequestsHistory.ContainsKey(senderId))
                _giveRequestsHistory[senderId] = new List<DateTime>();
            _giveRequestsHistory[senderId].Add(DateTime.UtcNow);

            if (!_ammoBurstHistory.ContainsKey(senderId))
                _ammoBurstHistory[senderId] = new List<int>();
            _ammoBurstHistory[senderId].Add(amount);
            if (_ammoBurstHistory[senderId].Count > 50)
                _ammoBurstHistory[senderId].RemoveAt(0);

            if (!_itemsTwoMinuteHistory.ContainsKey(senderId))
                _itemsTwoMinuteHistory[senderId] = new List<DateTime>();
            _itemsTwoMinuteHistory[senderId].Add(DateTime.UtcNow);
        }

        // ── Парсеры ──

        private bool TryParseScaleArgs(string[] args, out float maxVal, out bool isDummy)
        {
            maxVal = 0;
            isDummy = false;
            if (args.Length < 2) return false;

            if (TryParseTargetId(args[0], out int targetId))
            {
                var target = Player.Get(targetId);
                if (IsDummy(target))
                    isDummy = true;
            }

            for (int i = 1; i < args.Length; i++)
            {
                if (float.TryParse(args[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                {
                    if (Mathf.Abs(val) > maxVal) maxVal = Mathf.Abs(val);
                }
            }
            return maxVal > 0;
        }

        private bool TryParseColorArgs(string[] args, out float maxChannel)
        {
            maxChannel = 0;
            if (args.Length < 2) return false;

            for (int i = 1; i < args.Length; i++)
            {
                if (float.TryParse(args[i], NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                {
                    if (Mathf.Abs(val) > maxChannel) maxChannel = Mathf.Abs(val);
                }
            }
            return maxChannel > 0;
        }

        private bool TryParseAmmoAmount(string[] args, out int amount)
        {
            amount = 0;
            for (int i = 1; i < args.Length; i++)
            {
                if (int.TryParse(args[i], out int val) && val > 100)
                {
                    amount = val;
                    return true;
                }
            }
            return false;
        }
    }
}