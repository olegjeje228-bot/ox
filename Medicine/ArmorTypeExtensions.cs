namespace EventHUD.Medicine
{
    public static class ArmorTypeExtensions
    {
        public static string GetDisplayName(this ArmorType t) => t switch
        {
            ArmorType.Light  => "Лёгкий",
            ArmorType.Combat => "Боевой",
            ArmorType.Heavy  => "Тяжёлый",
            ArmorType.Tank   => "Танковый",
            _                => "Нет"
        };

        // Прочность брони = порог пробития калибром.
        // Пистолет (калибр 10) пробивает: Лёгкий 30/10=3, Боевой 90/10=9,
        // Тяжёлый 150/10=15, Танковый 210/10=21 пуль.
        public static float GetMaxDurability(this ArmorType t) => t switch
        {
            ArmorType.Light  => 30f,
            ArmorType.Combat => 90f,
            ArmorType.Heavy  => 150f,
            ArmorType.Tank   => 210f,
            _                => 0f
        };

        public static float GetBaseAbsorption(this ArmorType t) => t switch
        {
            ArmorType.Light  => 30f,
            ArmorType.Combat => 45f,
            ArmorType.Heavy  => 60f,
            ArmorType.Tank   => 75f,
            _                => 0f
        };

        /// <summary>
        /// Доля урона по HP, блокируемая пока броня цела (0..1).
        /// Пистолет по танку → почти 0 урона.
        /// </summary>
        public static float GetIntactDamageReduction(this ArmorType t) => t switch
        {
            ArmorType.Light  => 0.45f,
            ArmorType.Combat => 0.62f,
            ArmorType.Heavy  => 0.78f,
            ArmorType.Tank   => 0.90f,
            _                => 0f
        };

        /// <summary>
        /// Синий броне-щит (Hume Shield). Не восстанавливается, но принимает урон.
        /// Компенсация за нерф прочности брони.
        /// </summary>
        public static float GetArmorShield(this ArmorType t) => t switch
        {
            ArmorType.Light  => 20f,
            ArmorType.Combat => 40f,
            ArmorType.Heavy  => 60f,
            ArmorType.Tank   => 80f,
            _                => 0f
        };

        /// <summary>Защищает ли конечности (только танковый, на 50%)</summary>
        public static float GetLimbMultiplier(this ArmorType t) => t switch
        {
            ArmorType.Tank => 0.5f,
            _              => 0f
        };

        /// <summary>Есть ли шлем</summary>
        public static bool HasHelmet(this ArmorType t) => t switch
        {
            ArmorType.Combat => true,
            ArmorType.Heavy  => true,
            ArmorType.Tank   => true,
            _                => false
        };

        /// <summary>Порог пробития шлема</summary>
        public static float GetHelmetThreshold(this ArmorType t) => t switch
        {
            ArmorType.Combat => 38f,
            ArmorType.Heavy  => 50f,
            ArmorType.Tank   => 20f,  // 2 пули (калибр 10)
            _                => 0f
        };
    }
}
 