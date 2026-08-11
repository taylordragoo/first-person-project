using NUnit.Framework;
using UnityEngine;

namespace FirstPersonProject.Integrations.Kinemation.Presentation.EditModeTests
{
    public sealed class LocomotionPresentationEvaluatorTests
    {
        private static LocomotionPresentationSettings Settings =>
            LocomotionPresentationSettings.Default;

        private static LocomotionPresentationInput Input(
            GaitSource source = GaitSource.ResolvedRawGait)
        {
            return new LocomotionPresentationInput
            {
                GaitSource = source,
                IsAlive = true,
                IsGrounded = true,
                IsFirstPerson = true
            };
        }

        [Test]
        public void RawGaitSource_PreservesSuppliedGait()
        {
            var input = Input();
            input.RawGait = 2.35f;
            input.ObservedPlanarSpeed = 0.01f;

            var output = new LocomotionPresentationEvaluator().Evaluate(input, Settings, 0.1f);

            Assert.AreEqual(2.35f, output.RawPresentationGait, 0.0001f);
            Assert.AreEqual(2.35f, output.SmoothedAnimatorGait, 0.0001f);
        }

        [Test]
        public void SpeedSource_UsesWalkJogSprintCurve()
        {
            var evaluator = new LocomotionPresentationEvaluator();
            var input = Input(GaitSource.ObservedPlanarSpeed);
            input.IsMoving = true;

            input.ObservedPlanarSpeed = 1.5f;
            Assert.AreEqual(1f, evaluator.Evaluate(input, Settings, 0.1f).RawPresentationGait, 0.0001f);
            input.ObservedPlanarSpeed = 2.25f;
            Assert.AreEqual(1.5f, evaluator.Evaluate(input, Settings, 0.1f).RawPresentationGait, 0.0001f);
            input.ObservedPlanarSpeed = 4f;
            Assert.AreEqual(2.5f, evaluator.Evaluate(input, Settings, 0.1f).RawPresentationGait, 0.0001f);
            input.ObservedPlanarSpeed = 8f;
            Assert.AreEqual(3f, evaluator.Evaluate(input, Settings, 0.1f).RawPresentationGait, 0.0001f);
        }

        [Test]
        public void DirectionAndGaitSources_DoNotUseUnselectedValues()
        {
            var input = Input(GaitSource.ResolvedRawGait);
            input.RawGait = 1.25f;
            input.ObservedPlanarSpeed = 100f;
            input.MoveAxes = new Vector2(0.6f, 0.8f);
            input.IsFirstPerson = false;

            var settings = Settings;
            settings.AnimGaitSmoothing = 0f;
            settings.AnimatorMoveInterpSpeed = 0f;
            var output = new LocomotionPresentationEvaluator().Evaluate(input, settings, 0.1f);

            Assert.AreEqual(1.25f, output.RawPresentationGait, 0.0001f);
            Assert.AreEqual(Vector2.up, output.SmoothedAnimatorMoveAxes);
        }

        [Test]
        public void FirstSample_HasNoSyntheticEdges_AndLaterEdgesFireOnce()
        {
            var evaluator = new LocomotionPresentationEvaluator();
            var input = Input();
            input.IsMoving = true;
            input.IsCrouching = true;
            input.IsInAir = true;
            input.IsGrounded = false;
            input.IsAiming = true;
            input.IsSprinting = true;

            var first = evaluator.Evaluate(input, Settings, 0.1f);
            Assert.IsFalse(first.MovementChanged || first.CrouchChanged || first.Jumped
                || first.AimChanged || first.SprintChanged || first.Landed);

            var steady = evaluator.Evaluate(input, Settings, 0.1f);
            Assert.IsFalse(steady.MovementChanged || steady.CrouchChanged || steady.Jumped
                || steady.AimChanged || steady.SprintChanged || steady.Landed);

            input.IsMoving = false;
            input.IsCrouching = false;
            input.IsInAir = false;
            input.IsGrounded = true;
            input.IsAiming = false;
            input.IsSprinting = false;
            var second = evaluator.Evaluate(input, Settings, 0.1f);
            Assert.IsTrue(second.MovementStopped);
            Assert.IsTrue(second.CrouchStopped);
            Assert.IsTrue(second.Landed);
            Assert.IsTrue(second.AimStopped);
            Assert.IsTrue(second.SprintStopped);

            var third = evaluator.Evaluate(input, Settings, 0.1f);
            Assert.IsFalse(third.MovementChanged || third.CrouchChanged || third.Landed
                || third.AimChanged || third.SprintChanged);
        }

        [Test]
        public void DeadInput_IsNeutralAndSuppressesEdges()
        {
            var evaluator = new LocomotionPresentationEvaluator();
            var alive = Input();
            alive.IsMoving = true;
            alive.RawGait = 3f;
            alive.MoveAxes = Vector2.right;
            evaluator.Evaluate(alive, Settings, 0.1f);

            alive.IsAlive = false;
            var dead = evaluator.Evaluate(alive, Settings, 0.1f);

            Assert.AreEqual(0f, dead.RawPresentationGait);
            Assert.AreEqual(0f, dead.SmoothedAnimatorGait);
            Assert.AreEqual(Vector2.zero, dead.SmoothedAnimatorMoveAxes);
            Assert.IsFalse(dead.AnimatorIsMoving || dead.AnimatorIsCrouching || dead.AnimatorIsInAir);
            Assert.IsFalse(dead.MovementChanged || dead.CrouchChanged || dead.Jumped
                || dead.Landed || dead.AimChanged || dead.SprintChanged);
        }

        [Test]
        public void ExplicitDeltaTime_IsDeterministic()
        {
            var input = Input();
            var settings = Settings;
            settings.AnimGaitSmoothing = 5f;
            settings.AnimatorMoveInterpSpeed = 7f;

            var first = new LocomotionPresentationEvaluator();
            var second = new LocomotionPresentationEvaluator();
            first.Evaluate(input, settings, 0.1f);
            second.Evaluate(input, settings, 0.1f);

            input.RawGait = 2f;
            input.MoveAxes = Vector2.right;
            var a = first.Evaluate(input, settings, 0.25f);
            var b = second.Evaluate(input, settings, 0.25f);

            Assert.AreEqual(a.SmoothedAnimatorGait, b.SmoothedAnimatorGait, 0.000001f);
            Assert.AreEqual(a.SmoothedAnimatorMoveAxes, b.SmoothedAnimatorMoveAxes);
            Assert.AreEqual(2f * (1f - Mathf.Exp(-5f * 0.25f)),
                a.SmoothedAnimatorGait, 0.000001f);
            Assert.AreEqual(1f - Mathf.Exp(-7f * 0.25f),
                a.SmoothedAnimatorMoveAxes.x, 0.000001f);
        }

        [Test]
        public void GaitSources_UseIdenticalSmoothingAfterResolution()
        {
            var settings = Settings;
            var rawEvaluator = new LocomotionPresentationEvaluator();
            var speedEvaluator = new LocomotionPresentationEvaluator();
            var raw = Input(GaitSource.ResolvedRawGait);
            var speed = Input(GaitSource.ObservedPlanarSpeed);

            rawEvaluator.Evaluate(raw, settings, 0.1f);
            speedEvaluator.Evaluate(speed, settings, 0.1f);

            raw.RawGait = 1.5f;
            speed.IsMoving = true;
            speed.ObservedPlanarSpeed = 2.25f;
            var rawOutput = rawEvaluator.Evaluate(raw, settings, 0.2f);
            var speedOutput = speedEvaluator.Evaluate(speed, settings, 0.2f);

            Assert.AreEqual(rawOutput.RawPresentationGait,
                speedOutput.RawPresentationGait, 0.000001f);
            Assert.AreEqual(rawOutput.SmoothedAnimatorGait,
                speedOutput.SmoothedAnimatorGait, 0.000001f);
        }

        [Test]
        public void ZeroMoveSmoothing_PreservesPreviousAxesLikeCas()
        {
            var evaluator = new LocomotionPresentationEvaluator();
            var input = Input();
            var settings = Settings;
            settings.AnimatorMoveInterpSpeed = 0f;

            evaluator.Evaluate(input, settings, 0.1f);
            input.MoveAxes = Vector2.right;
            var output = evaluator.Evaluate(input, settings, 0.1f);

            Assert.AreEqual(Vector2.zero, output.SmoothedAnimatorMoveAxes);
        }

        [Test]
        public void Reset_BootstrapsWithoutEdges()
        {
            var evaluator = new LocomotionPresentationEvaluator();
            var input = Input();
            evaluator.Evaluate(input, Settings, 0.1f);
            input.IsMoving = true;
            evaluator.Evaluate(input, Settings, 0.1f);

            evaluator.Reset();
            var resetSample = evaluator.Evaluate(input, Settings, 0.1f);

            Assert.IsFalse(resetSample.MovementChanged || resetSample.CrouchChanged
                || resetSample.Jumped || resetSample.Landed || resetSample.AimChanged
                || resetSample.SprintChanged);
        }

        [Test]
        public void Evaluators_DoNotShareState()
        {
            var moving = Input();
            moving.IsMoving = true;
            moving.RawGait = 3f;

            var first = new LocomotionPresentationEvaluator();
            first.Evaluate(moving, Settings, 0.1f);

            var secondFirst = new LocomotionPresentationEvaluator().Evaluate(
                moving, Settings, 0.1f);

            Assert.AreEqual(3f, secondFirst.SmoothedAnimatorGait, 0.0001f);
            Assert.IsFalse(secondFirst.MovementStarted);
        }
    }
}
