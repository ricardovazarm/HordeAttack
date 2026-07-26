namespace HordeAttack
{
    /// <summary>Which of the player's hands something belongs to.</summary>
    /// <remarks>
    /// Kept as a value rather than derived from the transform because the things that need it —
    /// haptics above all — are addressed by hand, and the hierarchy a fist hangs from depends on
    /// whether the headset reports controllers or hand tracking.
    /// </remarks>
    public enum HandSide
    {
        Left,
        Right,
    }
}
