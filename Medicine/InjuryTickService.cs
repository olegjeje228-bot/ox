using System;
using System.Collections.Generic;
using Exiled.API.Features;
using EventHUD.Hud;
using MEC;

namespace EventHUD.Medicine
{
    /// <summary>
    /// Корутина — каждую секунду обходит всех игроков с кровотечениями и наносит урон.
    /// </summary>
    public class InjuryTickService
    {
        private CoroutineHandle _tickHandle;

        // Трекер пассивной фазы: когда последний раз наносили пассивный урон
        private readonly Dictionary<string, DateTime> _lastPassiveTick = new();

        public void Start()
        {
            _tickHandle = Timing.RunCoroutine(TickLoop());
        }

        public void Stop()
        {
            Timing.KillCoroutines(_tickHandle);
            _lastPassiveTick.Clear();
        }

        private IEnumerator<float> TickLoop()
        {
            while (true)
            {
                yield return Timing.WaitForSeconds(Plugin.Instance.Config.InjuryTickInterval);

                if (!Rpm.RpModuleManager.Instance.IsEnabled(Rpm.RpModuleType.Medicine))
                    continue;

                foreach (var player in Player.List)
                {
                    if (!player.IsAlive)
                        continue;

                    if (!MedicalStorage.TryGet(player.UserId, out var state))
                        continue;

                    // Проверка истечения первой помощи
                    if (state.FirstAidUsed && state.FirstAidOriginalBleeding.HasValue)
                    {
                        if (state.CheckFirstAidExpiry())
                        {
                            // Кровотечение восстановлено — сбрасываем тик-трекер
                            ResetPlayer(player.UserId);
                            HudNoticeService.Show(player, "<color=red>Временная повязка слетела! Кровотечение возобновилось.</color>", 3f);
                        }
                    }

                    var bleeding = state.GetBleedingLevel();
                    if (!bleeding.HasValue)
                        continue;

                    ProcessBleedingTick(player, state, bleeding.Value);
                }
            }
        }

        private void ProcessBleedingTick(Player player, PlayerMedicalState state, GlobalCondition bleedLevel)
        {
            var config = Plugin.Instance.Config;
            var now = DateTime.UtcNow;
            float elapsed = (float)(now - state.BleedingStartedAt).TotalSeconds;

            float burstDps, burstDuration, passiveDps, passiveInterval;

            switch (bleedLevel)
            {
                case GlobalCondition.BleedingLight:
                    burstDps        = config.LightBleedBurstDps;
                    burstDuration   = config.LightBleedBurstDuration;
                    passiveDps      = config.LightBleedPassiveDps;
                    passiveInterval = config.LightBleedPassiveInterval;
                    break;

                case GlobalCondition.BleedingMedium:
                    burstDps        = config.MediumBleedBurstDps;
                    burstDuration   = config.MediumBleedBurstDuration;
                    passiveDps      = config.MediumBleedPassiveDps;
                    passiveInterval = config.MediumBleedPassiveInterval;
                    break;

                case GlobalCondition.BleedingHeavy:
                    burstDps        = config.HeavyBleedBurstDps;
                    burstDuration   = config.HeavyBleedBurstDuration;
                    passiveDps      = config.HeavyBleedPassiveDps;
                    passiveInterval = config.HeavyBleedPassiveInterval;
                    break;

                default:
                    return;
            }

            float tickInterval = config.InjuryTickInterval;

            if (!state.BleedingBurstFinished)
            {
                // ── Бурная фаза ──
                if (elapsed < burstDuration)
                {
                    // Урон масштабируется под интервал тика, чтобы DPS оставался корректным
                    player.Hurt(burstDps * tickInterval, "Кровотечение");
                }
                else
                {
                    state.BleedingBurstFinished = true;
                    _lastPassiveTick[player.UserId] = now;
                }
            }
            else
            {
                // ── Пассивная фаза ──
                if (!_lastPassiveTick.TryGetValue(player.UserId, out var lastTick))
                {
                    _lastPassiveTick[player.UserId] = now;
                    return;
                }

                if ((now - lastTick).TotalSeconds >= passiveInterval)
                {
                    player.Hurt(passiveDps, "Кровотечение");
                    _lastPassiveTick[player.UserId] = now;
                }
            }
        }

        /// <summary>
        /// Сбросить трекер пассивной фазы для игрока (при лечении/смерти).
        /// </summary>
        public void ResetPlayer(string userId)
        {
            _lastPassiveTick.Remove(userId);
        }
    }
}
 