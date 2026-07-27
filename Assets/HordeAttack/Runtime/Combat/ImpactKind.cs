namespace HordeAttack
{
    /// <summary>What a flying enemy ran into.</summary>
    /// <remarks>
    /// The two are scored on separate speed bands rather than on one shared curve, because they are
    /// reached in completely different ways. A creature is only ever hit by something the player
    /// deliberately sent at it, while the floor is what <em>every</em> punched enemy lands on a
    /// second later — so the floor has to stay quiet at knockback landing speeds and still be
    /// lethal when someone spikes a gnome into it on purpose. See <see cref="ImpactSettings"/>.
    /// </remarks>
    public enum ImpactKind
    {
        /// <summary>Another enemy. Both of them take the hit.</summary>
        Creature,

        /// <summary>The ground, or anything else solid that is not a creature.</summary>
        Ground,
    }
}
