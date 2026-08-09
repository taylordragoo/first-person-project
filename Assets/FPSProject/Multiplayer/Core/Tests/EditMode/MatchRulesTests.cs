using FPSProject.Multiplayer.Core.Health;
using FPSProject.Multiplayer.Core.Match;
using NUnit.Framework;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.EditModeTests
{
    public class MatchRulesTests
    {
        [Test]
        public void Sanitize_ClampsHostSettingsToSupportedRange()
        {
            var settings = new MatchLaunchSettings
            {
                map = 99,
                alphaBotCount = 99,
                bravoBotCount = -5,
                durationSeconds = 1,
                preferredTeam = 99,
                defaultWeaponId = 0
            };

            settings = MatchRules.Sanitize(settings);

            Assert.AreEqual(MatchMap.Office, settings.Map);
            Assert.AreEqual(4, settings.alphaBotCount);
            Assert.AreEqual(0, settings.bravoBotCount);
            Assert.AreEqual(60, settings.durationSeconds);
            Assert.AreEqual(MatchTeam.Unassigned, settings.PreferredTeam);
            Assert.AreEqual(1, settings.DefaultWeaponId);
        }

        [Test]
        public void SelectAvailableTeam_RejectsOnlyTheRequestedFullSide()
        {
            Assert.AreEqual(MatchTeam.Unassigned,
                MatchRules.SelectAvailableTeam(MatchTeam.Alpha, 4, 1));
            Assert.AreEqual(MatchTeam.Bravo,
                MatchRules.SelectAvailableTeam(MatchTeam.Bravo, 4, 1));
            Assert.AreEqual(MatchTeam.Bravo,
                MatchRules.SelectAvailableTeam(MatchTeam.Unassigned, 4, 1));
        }

        [Test]
        public void SanitizeHostSettings_ReservesOneSlotOnTheHostsTeam()
        {
            MatchLaunchSettings settings = MatchLaunchSettings.Default;
            settings.alphaBotCount = 4;
            settings.bravoBotCount = 4;
            settings.preferredTeam = (int)MatchTeam.Bravo;

            settings = MatchRules.SanitizeHostSettings(settings);

            Assert.AreEqual(4, settings.alphaBotCount);
            Assert.AreEqual(3, settings.bravoBotCount);

            settings.preferredTeam = (int)MatchTeam.Alpha;
            settings = MatchRules.SanitizeHostSettings(settings);

            Assert.AreEqual(3, settings.alphaBotCount);
            Assert.AreEqual(3, settings.bravoBotCount);
        }

        [Test]
        public void GetBotTarget_UsesIndependentTeamSettingAndHumansTakePriority()
        {
            Assert.AreEqual(3, MatchRules.GetBotTarget(4, 1));
            Assert.AreEqual(2, MatchRules.GetBotTarget(2, 1));
            Assert.AreEqual(0, MatchRules.GetBotTarget(4, 4));
            Assert.AreEqual(4, MatchRules.GetBotTarget(99, -5));
        }

        [Test]
        public void GetWinner_ReturnsUnassignedForATie()
        {
            Assert.AreEqual(MatchTeam.Alpha, MatchRules.GetWinner(4, 2));
            Assert.AreEqual(MatchTeam.Bravo, MatchRules.GetWinner(1, 3));
            Assert.AreEqual(MatchTeam.Unassigned, MatchRules.GetWinner(5, 5));
        }

        [TestCase("dust2 Spawn Points", "Spawn Point 1", MatchMap.Dust2, MatchTeam.Alpha, 0)]
        [TestCase("dust2 Spawn Points", "Spawn Point 8", MatchMap.Dust2, MatchTeam.Bravo, 3)]
        [TestCase("Office Spawn Points", "Spawn Point 5", MatchMap.Office, MatchTeam.Bravo, 0)]
        [TestCase("Network Spawn Points", "Spawn Point 4", MatchMap.Dust2, MatchTeam.Alpha, 3)]
        public void SpawnCatalog_ClassifiesExistingNumberedHierarchy(string rootName,
            string pointName, MatchMap expectedMap, MatchTeam expectedTeam, int expectedSlot)
        {
            var root = new GameObject(rootName);
            var pointObject = new GameObject(pointName);
            pointObject.transform.SetParent(root.transform);
            NetworkSpawnPoint point = pointObject.AddComponent<NetworkSpawnPoint>();

            try
            {
                Assert.IsTrue(MatchSpawnCatalog.TryClassify(point, out MatchMap map,
                    out MatchTeam team, out int slot));
                Assert.AreEqual(expectedMap, map);
                Assert.AreEqual(expectedTeam, team);
                Assert.AreEqual(expectedSlot, slot);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
