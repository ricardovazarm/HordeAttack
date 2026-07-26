namespace HordeAttack
{
    /// <summary>Roughly where on the body a latch anchor sits.</summary>
    /// <remarks>
    /// Two bands rather than an exact height, because the only decision that reads this is which
    /// anchors a given <see cref="LatchStyle"/> prefers.
    /// </remarks>
    public enum LatchHeight
    {
        /// <summary>Legs, waist and arms — what an enemy arriving on foot can reach.</summary>
        Low,

        /// <summary>Chest and shoulders — what an enemy has to jump for.</summary>
        High,
    }
}
