// /Core/GPU/GpuCommandBuilder.cs
using System;
using System.Collections.Generic;
using Godot;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Core
{
    /// <summary>
    /// Utility for building and combining GPU command sequences.
    /// Handles the common pattern of executing multiple compute dispatches with barriers between them.
    /// Does not register its own debug class - uses caller's debug context.
    /// </summary>
    public static class GpuCommandBuilder
    {
        /// <summary>
        /// Combines multiple GPU commands into a single command with barriers between each.
        /// Barriers ensure proper ordering and data visibility between compute dispatches.
        /// </summary>
        /// <param name="commands">List of GPU commands to combine</param>
        /// <param name="addInitialBarrier">If true, adds a barrier before the first command</param>
        /// <param name="debugClassName">Optional debug class name for error logging</param>
        /// <returns>Combined command action or null if no valid commands</returns>
        public static Action<long> CombineCommands(
            List<Action<long>> commands, 
            bool addInitialBarrier = false,
            string debugClassName = null)
        {
            if (commands == null || commands.Count == 0)
                return null;

            return (computeList) =>
            {
                for (int i = 0; i < commands.Count; i++)
                {
                    if (i > 0 || addInitialBarrier)
                    {
                        Gpu.Rd.ComputeListAddBarrier(computeList);
                    }

                    try
                    {
                        commands[i]?.Invoke(computeList);
                    }
                    catch (Exception ex)
                    {
                        if (debugClassName != null)
                        {
                            DebugManager.Instance?.LogError(debugClassName,
                                $"Failed to execute GPU command {i}/{commands.Count}: {ex.Message}");
                        }
                        else
                        {
                            GD.PrintErr($"[GpuCommandBuilder] Failed to execute GPU command {i}/{commands.Count}: {ex.Message}");
                        }
                        break;
                    }
                }
            };
        }

        /// <summary>
        /// Combines command tuples (from kernel creation methods) into a single task-ready package.
        /// Useful for building multi-step GPU operations from individual kernel outputs.
        /// </summary>
        /// <param name="commandTuples">Variable number of (command, RIDs, shader) tuples</param>
        /// <returns>Tuple of (combined command, all RIDs, all shader paths)</returns>
        public static (Action<long> combined, List<Rid> allRids, List<string> allShaders) CombineCommandTuples(
            params (Action<long> cmd, List<Rid> rids, string shader)[] commandTuples)
        {
            var commands = new List<Action<long>>();
            var allRids = new List<Rid>();
            var allShaders = new List<string>();

            foreach (var (cmd, rids, shader) in commandTuples)
            {
                if (cmd != null)
                {
                    commands.Add(cmd);
                    allRids.AddRange(rids ?? new List<Rid>());
                    if (!string.IsNullOrEmpty(shader))
                        allShaders.Add(shader);
                }
            }

            return (CombineCommands(commands), allRids, allShaders);
        }
    }
}