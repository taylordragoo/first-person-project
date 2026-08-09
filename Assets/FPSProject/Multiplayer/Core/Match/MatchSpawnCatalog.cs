using System;
using System.Collections.Generic;
using FPSProject.Multiplayer.Core.Health;
using UnityEngine;

namespace FPSProject.Multiplayer.Core.Match
{
    public static class MatchSpawnCatalog
    {
        public const string Dust2SpawnRootName = "dust2 Spawn Points";
        public const string OfficeSpawnRootName = "Office Spawn Points";
        public const string LegacySpawnRootName = "Network Spawn Points";

        private static readonly Dictionary<(MatchMap, MatchTeam), List<NetworkSpawnPoint>> _cache
            = new Dictionary<(MatchMap, MatchTeam), List<NetworkSpawnPoint>>();
        private static readonly List<NetworkSpawnPoint> _emptyResults
            = new List<NetworkSpawnPoint>(0);
        private static bool _cacheBuilt;

        public static void InvalidateCache()
        {
            _cache.Clear();
            _cacheBuilt = false;
        }

        public static bool TryClassify(NetworkSpawnPoint spawnPoint, out MatchMap map,
            out MatchTeam team, out int teamSlot)
        {
            map = MatchMap.Dust2;
            team = MatchTeam.Unassigned;
            teamSlot = -1;
            if (spawnPoint == null || spawnPoint.transform.parent == null) return false;

            string parentName = spawnPoint.transform.parent.name;
            if (string.Equals(parentName, Dust2SpawnRootName,
                    StringComparison.OrdinalIgnoreCase))
            {
                map = MatchMap.Dust2;
            }
            else if (string.Equals(parentName, LegacySpawnRootName,
                         StringComparison.OrdinalIgnoreCase))
            {
                map = MatchMap.Dust2;
            }
            else if (string.Equals(parentName, OfficeSpawnRootName,
                         StringComparison.OrdinalIgnoreCase))
            {
                map = MatchMap.Office;
            }
            else
            {
                return false;
            }

            if (!TryReadSpawnNumber(spawnPoint.name, out int number)) return false;
            if (number >= 1 && number <= 4)
            {
                team = MatchTeam.Alpha;
                teamSlot = number - 1;
                return true;
            }

            if (number >= 5 && number <= 8)
            {
                team = MatchTeam.Bravo;
                teamSlot = number - 5;
                return true;
            }

            return false;
        }

        public static List<NetworkSpawnPoint> Find(MatchMap map, MatchTeam team)
        {
            if (!_cacheBuilt) BuildCache();

            if (_cache.TryGetValue((map, team), out List<NetworkSpawnPoint> cached))
                return cached;
            return _emptyResults;
        }

        private static void BuildCache()
        {
            _cache.Clear();
            NetworkSpawnPoint[] discovered = UnityEngine.Object.FindObjectsByType<NetworkSpawnPoint>(
                FindObjectsInactive.Include);

            Dictionary<(MatchMap, MatchTeam), List<NetworkSpawnPoint>> grouped
                = new Dictionary<(MatchMap, MatchTeam), List<NetworkSpawnPoint>>();

            foreach (NetworkSpawnPoint spawnPoint in discovered)
            {
                if (!TryClassify(spawnPoint, out MatchMap pointMap,
                        out MatchTeam pointTeam, out _)) continue;
                var key = (pointMap, pointTeam);
                if (!grouped.TryGetValue(key, out List<NetworkSpawnPoint> bucket))
                {
                    bucket = new List<NetworkSpawnPoint>(MatchLaunchSettings.MaxHumansPerTeam);
                    grouped[key] = bucket;
                }
                bucket.Add(spawnPoint);
            }

            foreach (var pair in grouped)
            {
                pair.Value.Sort(CompareTeamSlots);
                _cache[pair.Key] = pair.Value;
            }

            _cacheBuilt = true;
        }

        public static bool TryGetPoint(MatchMap map, MatchTeam team, int teamSlot,
            out NetworkSpawnPoint point)
        {
            List<NetworkSpawnPoint> points = Find(map, team);
            foreach (NetworkSpawnPoint candidate in points)
            {
                if (TryClassify(candidate, out _, out _, out int candidateSlot)
                    && candidateSlot == teamSlot)
                {
                    point = candidate;
                    return true;
                }
            }

            point = null;
            return false;
        }

        private static int CompareTeamSlots(NetworkSpawnPoint left,
            NetworkSpawnPoint right)
        {
            TryClassify(left, out _, out _, out int leftSlot);
            TryClassify(right, out _, out _, out int rightSlot);
            return leftSlot.CompareTo(rightSlot);
        }

        private static bool TryReadSpawnNumber(string objectName, out int number)
        {
            number = 0;
            if (string.IsNullOrWhiteSpace(objectName)) return false;
            int separator = objectName.LastIndexOf(' ');
            string suffix = separator >= 0 ? objectName.Substring(separator + 1) : objectName;
            return int.TryParse(suffix, out number);
        }
    }
}
