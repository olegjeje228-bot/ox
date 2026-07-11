using EventHUD.Medicine;
using Exiled.API.Features;
using PlayerRoles;

namespace EventHUD.Hud
{
    /// <summary>
    /// Строка "Состояние бронежилета: X%" в карточке игрока.
    /// Показывается когда аптечка НЕ в руках и у игрока есть бронежилет.
    /// Цвет градиентом: 100% = тускло-голубой (#6BAACC), 0% = красный (#FF0000).
    /// </summary>
    public static class ArmorHudSegment
    {
        public static string Build(Player player, Config c, float voffset)
        {
            var armor = ArmorStorage.GetOrCreate(player.UserId);

            // Проверяем, есть ли бронежилет в инвентаре
            bool hasArmorItem = false;
            foreach (var item in player.Items)
            {
                if (item.Type == ItemType.ArmorLight ||
                    item.Type == ItemType.ArmorCombat ||
                    item.Type == ItemType.ArmorHeavy)
                {
                    hasArmorItem = true;
                    break;
                }
            }

            // Если брони нет в инвентаре — сбрасываем состояние и не показываем
            if (!hasArmorItem)
            {
                if (armor.Type != ArmorType.None)
                    armor.Reset();
                return string.Empty;
            }

            // Если тип не установлен, определяем по инвентарю
            if (armor.Type == ArmorType.None)
            {
                foreach (var item in player.Items)
                {
                    if (item.Type == ItemType.ArmorLight ||
                        item.Type == ItemType.ArmorCombat ||
                        item.Type == ItemType.ArmorHeavy)
                    {
                        var armorType = item.Type switch
                        {
                            ItemType.ArmorHeavy when player.Role.Type == PlayerRoles.RoleTypeId.NtfCaptain => ArmorType.Tank,
                            ItemType.ArmorLight  => ArmorType.Light,
                            ItemType.ArmorCombat => ArmorType.Combat,
                            ItemType.ArmorHeavy  => ArmorType.Heavy,
                            _                    => ArmorType.None
                        };
                        if (armorType != ArmorType.None)
                            armor.SetType(armorType);
                        break;
                    }
                }
            }

            if (armor.Type == ArmorType.None)
                return string.Empty;

            float pct = armor.MaxDurability > 0
                ? (armor.Durability / armor.MaxDurability) * 100f
                : 0f;

            if (pct < 0f) pct = 0f;
            if (pct > 100f) pct = 100f;

            string color = InterpolateColor(pct);
            string pctText = armor.IsBroken ? "Разрушен" : $"{pct:0}%";

            return $"<voffset={voffset}em><indent={c.MedicineHudIndent}%>" +
                   $"<color={color}>Состояние бронежилета: {pctText}</color>";
        }

        /// <summary>
        /// Линейная интерполяция цвета от красного (0%) до тускло-голубого (100%).
        /// 0%   = #FF0000 (красный)
        /// 100% = #6BAACC (тускло-голубой)
        /// </summary>
        private static string InterpolateColor(float percent)
        {
            float t = percent / 100f;

            // От красного к тускло-голубому
            int r = (int)(255 + (107 - 255) * t); // 255 -> 107
            int g = (int)(0 + (170 - 0) * t);     // 0 -> 170
            int b = (int)(0 + (204 - 0) * t);     // 0 -> 204

            if (r < 0) r = 0; if (r > 255) r = 255;
            if (g < 0) g = 0; if (g > 255) g = 255;
            if (b < 0) b = 0; if (b > 255) b = 255;

            return $"#{r:X2}{g:X2}{b:X2}";
        }
    }
}
