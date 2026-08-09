using System;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.Match
{
    public enum MatchTeam : byte
    {
        Unassigned = 0,
        Alpha = 1,
        Bravo = 2
    }

    public enum MatchMap : byte
    {
        Dust2 = 0,
        Office = 1
    }

    public enum MatchPhase : byte
    {
        Waiting = 0,
        Running = 1,
        Finished = 2
    }

    [Serializable]
    public struct MatchLaunchSettings
    {
        public const int MaxHumansPerTeam = 4;
        public const int MaxCombatants = MaxHumansPerTeam * 2;

        public int map;
        public int alphaBotCount;
        public int bravoBotCount;
        public int durationSeconds;
        public int preferredTeam;
        public int defaultWeaponId;

        public static MatchLaunchSettings Default => new MatchLaunchSettings
        {
            map = (int)MatchMap.Dust2,
            alphaBotCount = 3,
            bravoBotCount = 4,
            durationSeconds = 300,
            preferredTeam = (int)MatchTeam.Alpha,
            defaultWeaponId = 1
        };

        public MatchMap Map => (MatchMap)map;
        public MatchTeam PreferredTeam => (MatchTeam)preferredTeam;
        public ushort DefaultWeaponId => (ushort)defaultWeaponId;
    }

    public static class MatchRules
    {
        public static MatchLaunchSettings Sanitize(MatchLaunchSettings settings)
        {
            settings.map = Mathf.Clamp(settings.map, (int)MatchMap.Dust2,
                (int)MatchMap.Office);
            settings.alphaBotCount = Mathf.Clamp(settings.alphaBotCount, 0,
                MatchLaunchSettings.MaxHumansPerTeam);
            settings.bravoBotCount = Mathf.Clamp(settings.bravoBotCount, 0,
                MatchLaunchSettings.MaxHumansPerTeam);
            settings.durationSeconds = Mathf.Clamp(settings.durationSeconds, 60, 3600);
            settings.preferredTeam = IsPlayableTeam((MatchTeam)settings.preferredTeam)
                ? settings.preferredTeam
                : (int)MatchTeam.Unassigned;
            settings.defaultWeaponId = Mathf.Clamp(settings.defaultWeaponId, 1,
                ushort.MaxValue);
            return settings;
        }

        public static MatchLaunchSettings SanitizeHostSettings(
            MatchLaunchSettings settings)
        {
            settings = Sanitize(settings);
            int hostTeamBotLimit = MatchLaunchSettings.MaxHumansPerTeam - 1;

            if (settings.PreferredTeam == MatchTeam.Alpha)
                settings.alphaBotCount = Mathf.Min(settings.alphaBotCount,
                    hostTeamBotLimit);
            else if (settings.PreferredTeam == MatchTeam.Bravo)
                settings.bravoBotCount = Mathf.Min(settings.bravoBotCount,
                    hostTeamBotLimit);

            return settings;
        }

        public static bool IsPlayableTeam(MatchTeam team)
        {
            return team == MatchTeam.Alpha || team == MatchTeam.Bravo;
        }

        public static bool CanHumanJoin(int currentHumans)
        {
            return currentHumans < MatchLaunchSettings.MaxHumansPerTeam;
        }

        public static MatchTeam SelectAvailableTeam(MatchTeam preferred,
            int alphaHumans, int bravoHumans)
        {
            if (IsPlayableTeam(preferred))
            {
                int preferredCount = preferred == MatchTeam.Alpha ? alphaHumans : bravoHumans;
                return CanHumanJoin(preferredCount) ? preferred : MatchTeam.Unassigned;
            }

            if (!CanHumanJoin(alphaHumans) && !CanHumanJoin(bravoHumans))
                return MatchTeam.Unassigned;
            if (!CanHumanJoin(alphaHumans)) return MatchTeam.Bravo;
            if (!CanHumanJoin(bravoHumans)) return MatchTeam.Alpha;
            return alphaHumans <= bravoHumans ? MatchTeam.Alpha : MatchTeam.Bravo;
        }

        public static int GetBotTarget(int configuredBots, int humans)
        {
            configuredBots = Mathf.Clamp(configuredBots, 0,
                MatchLaunchSettings.MaxHumansPerTeam);
            humans = Mathf.Clamp(humans, 0,
                MatchLaunchSettings.MaxHumansPerTeam);
            return Mathf.Min(configuredBots,
                MatchLaunchSettings.MaxHumansPerTeam - humans);
        }

        public static MatchTeam GetWinner(int alphaScore, int bravoScore)
        {
            if (alphaScore == bravoScore) return MatchTeam.Unassigned;
            return alphaScore > bravoScore ? MatchTeam.Alpha : MatchTeam.Bravo;
        }
    }
}
