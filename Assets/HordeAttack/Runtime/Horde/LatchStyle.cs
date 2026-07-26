namespace HordeAttack
{
    /// <summary>How an enemy goes about getting hold of the player.</summary>
    /// <remarks>
    /// Mixing both styles in the same wave is what makes the horde read as a swarm rather than as a
    /// queue: some of them come up your body while others are still arriving at your legs. See
    /// <c>PLAN.md</c>, Fase 2a.
    /// </remarks>
    public enum LatchStyle
    {
        /// <summary>Walks in and takes hold low — legs, waist, arms.</summary>
        Clinger,

        /// <summary>Jumps at you from a distance and takes hold high — chest, shoulders.</summary>
        Leaper,
    }
}
