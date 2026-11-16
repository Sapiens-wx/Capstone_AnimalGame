using System.Collections.Generic;
using AnimalGame.MapTest;
using AnimalGame.World;
using UnityEngine;

namespace AnimalGame.RobotMap
{
    internal enum RobotUnstuckResult { AlreadyClear, Found, NoSafePosition }

    internal static class RobotUnstuckSearch
    {
        private const float SampleSpacingMeters = 0.5f;
        private const float SearchRadiusMeters = 8f;

        internal static RobotUnstuckResult Find(RobotMover mover, HeightMapTraversalEvaluator evaluator,
            MapTestSceneController map, out Vector2 destination)
        {
            Vector2 startWorld = mover.transform.position;
            Vector2 startMap = InteractionShape.ToQuery(startWorld, map);
            InteractionShape startBody = mover.GetCollisionShapeAt(startWorld, mover.transform.rotation, map,
                mover.CollisionSkin);
            Rect startBodyBounds = WorldInteractionQuadTree.BoundsOf(startBody);
            Vector2 mapSize = map.MapSizeMeters;
            Vector2 forward = mover.Forward;
            Vector2 exitOffset = map.WorldDirectionToMapDirection(forward) * evaluator.RobotFootprintLengthMeters;

            destination = startWorld;
            if (map.TrySampleMapPosition(startMap, out _) && CanDriveAway(startMap, startWorld))
                return RobotUnstuckResult.AlreadyClear;

            foreach (Vector2Int offset in GetCandidateOffsets())
            {
                Vector2 candidateMap = startMap + (Vector2)offset * SampleSpacingMeters;
                if (!map.TrySampleMapPosition(candidateMap, out _)) continue;

                Vector2 candidateWorld = map.MapPositionToWorld(candidateMap);
                if (!CanDriveAway(candidateMap, candidateWorld)) continue;
                // The sweep allows the starting overlap to clear while blocking obstacles along the jump.
                if (mover.QueryCollisionSweep(startWorld, candidateWorld, map)) continue;

                destination = candidateWorld;
                return RobotUnstuckResult.Found;
            }

            return RobotUnstuckResult.NoSafePosition;

            bool CanDriveAway(Vector2 mapPosition, Vector2 worldPosition)
            {
                if (!BodyFitsMap(mapPosition)) return false;
                InteractionShape body = startBody.Translated(mapPosition - startMap);
                if (WorldInteractionQuery.Query(body, InteractionPushPlan.Solids, map, mover.gameObject.scene,
                        ignore: mover.transform))
                    return false;

                return CanDrive(mapPosition, worldPosition, 1f) || CanDrive(mapPosition, worldPosition, -1f);
            }

            bool CanDrive(Vector2 mapPosition, Vector2 worldPosition, float direction)
            {
                Vector2 exit = mapPosition + exitOffset * direction;
                return BodyFitsMap(exit)
                    && IsStable(evaluator.EvaluateCurrentSurface(worldPosition, forward * direction))
                    && IsStable(evaluator.EvaluateMapPath(mapPosition, exit));
            }

            bool IsStable(SlopeTraversalResult result) => result.HasData && result.IsPassable
                && !result.RequiresHardStop
                && evaluator.ClassifyUphillSlope(result.MaximumSurfaceSlopeAngle) != UphillSlopeLevel.LevelThree;

            bool BodyFitsMap(Vector2 mapPosition)
            {
                Vector2 offset = mapPosition - startMap;
                Vector2 minimum = startBodyBounds.min + offset;
                Vector2 maximum = startBodyBounds.max + offset;
                return minimum.x >= 0f && minimum.y >= 0f && maximum.x <= mapSize.x && maximum.y <= mapSize.y;
            }
        }

        private static List<Vector2Int> GetCandidateOffsets()
        {
            int radius = Mathf.FloorToInt(SearchRadiusMeters / SampleSpacingMeters);
            var offsets = new List<Vector2Int>();
            for (int x = -radius; x <= radius; x++)
            for (int y = -radius; y <= radius; y++)
            {
                int distanceSquared = x * x + y * y;
                if (distanceSquared > 0 && distanceSquared <= radius * radius)
                    offsets.Add(new Vector2Int(x, y));
            }

            offsets.Sort((first, second) =>
            {
                int order = first.sqrMagnitude.CompareTo(second.sqrMagnitude);
                if (order != 0) return order;
                order = first.x.CompareTo(second.x);
                return order != 0 ? order : first.y.CompareTo(second.y);
            });
            return offsets;
        }
    }
}
