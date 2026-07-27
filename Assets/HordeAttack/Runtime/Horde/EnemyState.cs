namespace HordeAttack
{
    /// <summary>What an enemy is currently doing.</summary>
    /// <remarks>
    /// Deliberately small. It grows one value per mechanic that actually exists: Fase 2a needed the
    /// enemy that is coming for you, the one mid-air on its way onto you, the one that already has
    /// you and the one that is out of the fight, and Fase 2b added the one you are holding.
    /// <c>Staggered</c> and the rest of the machine arrive in Fase 3, once waves exist to justify
    /// them.
    /// </remarks>
    public enum EnemyState
    {
        /// <summary>Advancing toward the nearest player.</summary>
        Walking,

        /// <summary>In the air, committed to an arc that ends on a latch anchor.</summary>
        Leaping,

        /// <summary>Attached to a player, riding along with them.</summary>
        Latched,

        /// <summary>Held by a player's grip, on its way to being thrown at something.</summary>
        Grabbed,

        /// <summary>Out of health.</summary>
        Dead,
    }
}
