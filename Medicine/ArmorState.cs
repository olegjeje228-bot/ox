using System;
using System.Collections.Generic;

namespace EventHUD.Medicine
{
    /// <summary>
    /// Состояние бронежилета v2 — прочность + поглощение + деградация.
    /// </summary>
    public class ArmorState
    {
        public ArmorType Type { get; set; } = ArmorType.None;
        public float MaxDurability { get; set; }
        public float Durability { get; set; }
        public float BaseAbsorption { get; set; } // Базовое поглощение %

        // Стаки контузии корпуса
        public List<DateTime> BodyStunStacks { get; } = new List<DateTime>();
        public bool HasAsphyxia { get; set; }
        public bool HasLungBruise { get; set; }

        // SCP-106
        public DateTime? Scp106TouchTime { get; set; }

        public bool IsBroken => Durability <= 0;

        /// <summary>
        /// Текущее поглощение с учётом деградации.
        /// </summary>
        public float EffectiveAbsorption
        {
            get
            {
                if (Durability <= 0) return 0f;
                float pct = Durability / MaxDurability;
                if (pct > 0.7f) return BaseAbsorption;
                if (pct > 0.3f) return BaseAbsorption - 5f; // -5%
                return BaseAbsorption - 15f; // -15%
            }
        }

        /// <summary>
        /// Статус прочности для HUD.
        /// </summary>
        public string DurabilityStatus
        {
            get
            {
                if (Durability <= 0) return "<color=#FF0000>Разрушена</color>";
                float pct = Durability / MaxDurability;
                if (pct > 0.7f) return "<color=#00FF00>Цела</color>";
                if (pct > 0.3f) return "<color=#FFAA00>Повреждена</color>";
                return "<color=#FF4444>Критическая</color>";
            }
        }

        /// <summary>
        /// Очистить просроченные стаки (старше 10 сек).
        /// </summary>
        public void CleanExpiredStacks()
        {
            var now = DateTime.UtcNow;
            BodyStunStacks.RemoveAll(t => (now - t).TotalSeconds >= 10.0);

            if (BodyStunStacks.Count < 4)
                HasLungBruise = false;
            if (BodyStunStacks.Count < 5)
                HasAsphyxia = false;
        }

        public int ActiveStacks
        {
            get
            {
                CleanExpiredStacks();
                return BodyStunStacks.Count;
            }
        }

        public void AddStun()
        {
            BodyStunStacks.Add(DateTime.UtcNow);
            if (ActiveStacks >= 5)
                HasAsphyxia = true;
            else if (ActiveStacks >= 4)
                HasLungBruise = true;
        }

        public void Reset()
        {
            Type = ArmorType.None;
            MaxDurability = 0;
            Durability = 0;
            BaseAbsorption = 0;
            BodyStunStacks.Clear();
            HasAsphyxia = false;
            HasLungBruise = false;
            Scp106TouchTime = null;
        }

        public void SetType(ArmorType type)
        {
            Type = type;
            MaxDurability = type.GetMaxDurability();
            Durability = MaxDurability;
            BaseAbsorption = type.GetBaseAbsorption();
        }
    }

    public static class ArmorStorage
    {
        private static readonly Dictionary<string, ArmorState> _states = new();

        public static ArmorState GetOrCreate(string userId)
        {
            if (!_states.TryGetValue(userId, out var state))
            {
                state = new ArmorState();
                _states[userId] = state;
            }
            return state;
        }

        public static bool TryGet(string userId, out ArmorState state) =>
            _states.TryGetValue(userId, out state);

        public static void Remove(string userId) => _states.Remove(userId);
        public static void ClearAll() => _states.Clear();
    }

    /// <summary>
    /// Прочность конкретного физического бронежилета (ключ — Serial предмета).
    /// Чтобы брошенный пробитый броник не становился "как новый" при подборе.
    /// </summary>
    public static class ArmorItemDurabilityStorage
    {
        private static readonly Dictionary<ushort, float> _durability = new();

        public static void Set(ushort serial, float durability) => _durability[serial] = durability;

        public static bool TryGet(ushort serial, out float durability) =>
            _durability.TryGetValue(serial, out durability);

        public static void ClearAll() => _durability.Clear();
    }
}
 