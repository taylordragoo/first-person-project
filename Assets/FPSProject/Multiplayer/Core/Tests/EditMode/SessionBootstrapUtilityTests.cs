using FPSProject.Multiplayer.Core.Bootstrap;
using NUnit.Framework;

namespace FPSProject.Multiplayer.Core.EditModeTests
{
    public class SessionBootstrapUtilityTests
    {
        [TestCase(" ab12cd ", "AB12CD")]
        [TestCase("", "")]
        [TestCase(null, "")]
        public void NormalizeJoinCode_TrimsAndUppercases(string input, string expected)
        {
            Assert.AreEqual(expected, SessionBootstrapUtility.NormalizeJoinCode(input));
        }

        [Test]
        public void BuildAuthenticationProfile_ProducesValidBoundedProfile()
        {
            string profile = SessionBootstrapUtility.BuildAuthenticationProfile(
                " player one! with a very long invalid profile name ", "fallback");
            Assert.LessOrEqual(profile.Length, 30);
            StringAssert.DoesNotContain(" ", profile);
            StringAssert.DoesNotContain("!", profile);
        }

        [Test]
        public void CommandLineHelpers_ReadFlagValueAndPositiveInt()
        {
            string[] args = { "game.exe", "-fpsAutoClient", "-fpsSessionCode=abc123",
                "-fpsProfilePlayers", "8" };
            Assert.IsTrue(SessionBootstrapUtility.HasCommandLineFlag(args, "-fpsAutoClient"));
            Assert.AreEqual("abc123", SessionBootstrapUtility.GetCommandLineValue(
                args, "-fpsSessionCode"));
            Assert.AreEqual(8, SessionBootstrapUtility.GetPositiveCommandLineInt(
                args, "-fpsProfilePlayers", 2));
        }

        [Test]
        public void MultiplayerMenuBridge_RejectsEmptyJoinCodeBeforeLoadingScene()
        {
            Assert.IsFalse(MultiplayerMenuBridge.LaunchClient("  "));
            Assert.AreEqual("Enter a session code before joining.",
                MultiplayerMenuBridge.LastError);
        }
    }
}
