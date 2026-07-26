using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace HordeAttack.Tests
{
    /// <summary>
    /// Tests for the rules that decide where on the player an arriving enemy takes hold.
    /// </summary>
    /// <remarks>
    /// Written against plain values rather than a built rig on purpose: the interesting cases here
    /// are a crouching player and a full body, and staging either with real transforms would take
    /// more setup than the rule being checked.
    /// </remarks>
    public class LatchAnchorSelectorTests
    {
        static readonly Vector3 k_Body = Vector3.zero;
        static readonly Vector3 k_Head = new Vector3(0f, 1.7f, 0f);

        const float k_Clearance = LatchAnchorSelector.k_DefaultHeadClearance;

        static List<LatchAnchorSlot> Slots(params LatchAnchorSlot[] slots) =>
            new List<LatchAnchorSlot>(slots);

        static LatchAnchorSlot Free(Vector3 position, LatchHeight height) =>
            new LatchAnchorSlot(position, height, true);

        static LatchAnchorSlot Taken(Vector3 position, LatchHeight height) =>
            new LatchAnchorSlot(position, height, false);

        /// <summary>An enemy standing off to the player's right, well clear of the body.</summary>
        static readonly Vector3 k_FromTheRight = new Vector3(3f, 0f, 0f);

        [Test]
        public void PreferredHeight_SendsLeapersHighAndClingersLow()
        {
            Assert.That(LatchAnchorSelector.PreferredHeight(LatchStyle.Leaper), Is.EqualTo(LatchHeight.High));
            Assert.That(LatchAnchorSelector.PreferredHeight(LatchStyle.Clinger), Is.EqualTo(LatchHeight.Low));
        }

        [Test]
        public void Select_GivesALeaperTheHighAnchorEvenWhenALowOneIsNearer()
        {
            var slots = Slots(
                Free(new Vector3(0.14f, 0.4f, 0f), LatchHeight.Low),
                Free(new Vector3(0.2f, 1.2f, 0f), LatchHeight.High));

            int index = LatchAnchorSelector.Select(
                slots, LatchStyle.Leaper, k_Body, k_FromTheRight, k_Head, k_Clearance);

            Assert.That(index, Is.EqualTo(1), "A leaper jumped onto a leg.");
        }

        [Test]
        public void Select_GivesAClingerTheLowAnchor()
        {
            var slots = Slots(
                Free(new Vector3(0.2f, 1.2f, 0f), LatchHeight.High),
                Free(new Vector3(0.14f, 0.4f, 0f), LatchHeight.Low));

            int index = LatchAnchorSelector.Select(
                slots, LatchStyle.Clinger, k_Body, k_FromTheRight, k_Head, k_Clearance);

            Assert.That(index, Is.EqualTo(1), "A creature that walked up ended up on the player's chest.");
        }

        /// <summary>
        /// Standing still and letting go of a preference beats standing still and doing nothing. By
        /// Fase 3 there are far more enemies than anchors, and a leaper that refuses every low
        /// anchor would simply stop at the player's feet.
        /// </summary>
        [Test]
        public void Select_FallsBackToTheOtherBandWhenItsOwnIsFull()
        {
            var slots = Slots(
                Taken(new Vector3(0.2f, 1.2f, 0f), LatchHeight.High),
                Free(new Vector3(0.14f, 0.4f, 0f), LatchHeight.Low));

            int index = LatchAnchorSelector.Select(
                slots, LatchStyle.Leaper, k_Body, k_FromTheRight, k_Head, k_Clearance);

            Assert.That(index, Is.EqualTo(1));
        }

        [Test]
        public void Select_NeverPicksAnAnchorSomeoneElseIsOn()
        {
            var slots = Slots(
                Taken(new Vector3(0.2f, 1.2f, 0f), LatchHeight.High),
                Taken(new Vector3(-0.2f, 1.2f, 0f), LatchHeight.High),
                Free(new Vector3(0.14f, 0.4f, 0f), LatchHeight.Low));

            int index = LatchAnchorSelector.Select(
                slots, LatchStyle.Leaper, k_Body, k_FromTheRight, k_Head, k_Clearance);

            Assert.That(index, Is.EqualTo(2),
                "Two enemies on the same anchor look like one flickering creature, not two.");
        }

        [Test]
        public void Select_ReturnsNothingWhenEveryAnchorIsTaken()
        {
            var slots = Slots(
                Taken(new Vector3(0.2f, 1.2f, 0f), LatchHeight.High),
                Taken(new Vector3(0.14f, 0.4f, 0f), LatchHeight.Low));

            int index = LatchAnchorSelector.Select(
                slots, LatchStyle.Clinger, k_Body, k_FromTheRight, k_Head, k_Clearance);

            Assert.That(index, Is.EqualTo(-1));
        }

        /// <summary>
        /// The comfort guard, and the reason it cannot be a tuning preference: a creature hanging in
        /// front of the visor is the fastest way to make somebody take the headset off.
        /// </summary>
        [Test]
        public void Select_RefusesAnAnchorTooCloseToTheHead()
        {
            var slots = Slots(
                Free(new Vector3(0f, 1.55f, 0f), LatchHeight.High),
                Free(new Vector3(0.14f, 0.4f, 0f), LatchHeight.Low));

            int index = LatchAnchorSelector.Select(
                slots, LatchStyle.Leaper, k_Body, k_FromTheRight, k_Head, k_Clearance);

            Assert.That(index, Is.EqualTo(1),
                "An anchor 15 cm from the player's eyes was offered to a leaper.");
        }

        [Test]
        public void Select_ReturnsNothingWhenEveryAnchorIsTooCloseToTheHead()
        {
            var slots = Slots(
                Free(new Vector3(0f, 1.6f, 0f), LatchHeight.High),
                Free(new Vector3(0.1f, 1.65f, 0f), LatchHeight.Low));

            int index = LatchAnchorSelector.Select(
                slots, LatchStyle.Leaper, k_Body, k_FromTheRight, k_Head, k_Clearance);

            Assert.That(index, Is.EqualTo(-1),
                "Refusing to latch at all is the correct answer when the only places left are in " +
                "the player's face.");
        }

        [Test]
        public void Select_PrefersTheSideTheEnemyIsArrivingFrom()
        {
            var slots = Slots(
                Free(new Vector3(-0.24f, 0.9f, 0f), LatchHeight.Low),
                Free(new Vector3(0.24f, 0.9f, 0f), LatchHeight.Low));

            int fromRight = LatchAnchorSelector.Select(
                slots, LatchStyle.Clinger, k_Body, k_FromTheRight, k_Head, k_Clearance);
            int fromLeft = LatchAnchorSelector.Select(
                slots, LatchStyle.Clinger, k_Body, -k_FromTheRight, k_Head, k_Clearance);

            Assert.That(fromRight, Is.EqualTo(1), "An enemy arriving from the right grabbed the left hip.");
            Assert.That(fromLeft, Is.EqualTo(0), "An enemy arriving from the left grabbed the right hip.");
        }

        /// <summary>
        /// Side is a preference, not a rule. A creature that walked around the player must still be
        /// able to take the only free anchor even if it is on the far side.
        /// </summary>
        [Test]
        public void Select_TakesTheWrongSideRatherThanNothing()
        {
            var slots = Slots(
                Free(new Vector3(-0.24f, 0.9f, 0f), LatchHeight.Low),
                Taken(new Vector3(0.24f, 0.9f, 0f), LatchHeight.Low));

            int index = LatchAnchorSelector.Select(
                slots, LatchStyle.Clinger, k_Body, k_FromTheRight, k_Head, k_Clearance);

            Assert.That(index, Is.EqualTo(0));
        }

        [Test]
        public void Select_IgnoresHowFarAwayTheEnemyIsWhenChoosingASide()
        {
            var slots = Slots(
                Free(new Vector3(-0.24f, 0.9f, 0f), LatchHeight.Low),
                Free(new Vector3(0.24f, 0.9f, 0f), LatchHeight.Low));

            int near = LatchAnchorSelector.Select(
                slots, LatchStyle.Clinger, k_Body, new Vector3(0.8f, 0f, 0f), k_Head, k_Clearance);
            int far = LatchAnchorSelector.Select(
                slots, LatchStyle.Clinger, k_Body, new Vector3(9f, 0f, 0f), k_Head, k_Clearance);

            Assert.That(near, Is.EqualTo(far),
                "Which side an enemy is on should not depend on how far along that side it is.");
        }

        [Test]
        public void Select_IsRepeatableWhenNothingDistinguishesTwoAnchors()
        {
            var slots = Slots(
                Free(new Vector3(0f, 0.9f, 0f), LatchHeight.Low),
                Free(new Vector3(0f, 0.9f, 0f), LatchHeight.Low));

            int first = LatchAnchorSelector.Select(
                slots, LatchStyle.Clinger, k_Body, k_FromTheRight, k_Head, k_Clearance);
            int second = LatchAnchorSelector.Select(
                slots, LatchStyle.Clinger, k_Body, k_FromTheRight, k_Head, k_Clearance);

            Assert.That(first, Is.EqualTo(0));
            Assert.That(second, Is.EqualTo(first), "The same question got two different answers.");
        }

        [Test]
        public void Select_CopesWithAnEnemyStandingExactlyOnThePlayer()
        {
            var slots = Slots(Free(new Vector3(0.24f, 0.9f, 0f), LatchHeight.Low));

            int index = LatchAnchorSelector.Select(
                slots, LatchStyle.Clinger, k_Body, k_Body, k_Head, k_Clearance);

            Assert.That(index, Is.EqualTo(0),
                "With no direction to arrive from, the anchor should still be offered.");
        }

        [Test]
        public void Select_CopesWithNothingToChooseFrom()
        {
            Assert.That(LatchAnchorSelector.Select(null, LatchStyle.Clinger, k_Body, k_FromTheRight, k_Head), Is.EqualTo(-1));
            Assert.That(LatchAnchorSelector.Select(Slots(), LatchStyle.Clinger, k_Body, k_FromTheRight, k_Head), Is.EqualTo(-1));
        }

        [Test]
        public void IsClearOfHead_MeasuresInEveryDirection()
        {
            Assert.That(LatchAnchorSelector.IsClearOfHead(new Vector3(0f, 1.5f, 0f), k_Head, k_Clearance), Is.False,
                "20 cm below the eyes is inside the clearance.");
            Assert.That(LatchAnchorSelector.IsClearOfHead(new Vector3(0.2f, 1.7f, 0f), k_Head, k_Clearance), Is.False,
                "20 cm to the side of the eyes is inside the clearance.");
            Assert.That(LatchAnchorSelector.IsClearOfHead(new Vector3(0f, 1.2f, 0f), k_Head, k_Clearance), Is.True);
        }

        [Test]
        public void IsClearOfHead_TreatsNoClearanceAsNoGuard()
        {
            Assert.That(LatchAnchorSelector.IsClearOfHead(k_Head, k_Head, 0f), Is.True);
        }

        /// <summary>
        /// A crouching player brings their head down toward anchors that are placed as a fraction of
        /// standing eye height. The right outcome is that the high anchors quietly stop being
        /// offered and leapers land on legs, not that a creature ends up on the player's face.
        /// </summary>
        [Test]
        public void Select_KeepsWorkingWhenThePlayerCrouches()
        {
            var crouchedHead = new Vector3(0f, 0.95f, 0f);
            var slots = Slots(
                Free(new Vector3(0.2f, 0.85f, 0.1f), LatchHeight.High),
                Free(new Vector3(0.14f, 0.25f, 0f), LatchHeight.Low));

            int index = LatchAnchorSelector.Select(
                slots, LatchStyle.Leaper, k_Body, k_FromTheRight, crouchedHead, k_Clearance);

            Assert.That(index, Is.EqualTo(1),
                "Crouching put a leaper's target inside the head clearance and it was still offered.");
        }
    }
}
