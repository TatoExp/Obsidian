﻿using Obsidian.API;
using Obsidian.ChunkData;
using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;
using System;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public class SnowyTaigaDecorator : BaseDecorator
    {
        public SnowyTaigaDecorator(Biomes biome, Chunk chunk, Vector surfacePos, BaseBiomeNoise noise) : base(biome, chunk, surfacePos, noise)
        {
        }

        public override void Decorate()
        {
            if (pos.Y < noise.settings.WaterLevel)
            {
                FillWater();
                return;
            }

            int worldX = (chunk.X << 4) + pos.X;
            int worldZ = (chunk.Z << 4) + pos.Z;

            var grass = Registry.GetBlock(Material.Snow);
            var dirt = Registry.GetBlock(Material.Dirt);

            chunk.SetBlock(pos, grass);
            for (int y = -1; y > -4; y--)
                chunk.SetBlock(pos + (0, y, 0), dirt);

            var sand = Registry.GetBlock(Material.SnowBlock);
            var sandstone = Registry.GetBlock(Material.PackedIce);
            var deadbush = Registry.GetBlock(Material.Snow);
            var cactus = Registry.GetBlock(Material.FrostedIce);

            for (int y = 0; y > -4; y--)
                chunk.SetBlock(pos + (0, y, 0), sand);
            for (int y = -4; y > -7; y--)
                chunk.SetBlock(pos + (0, y, 0), sandstone);

            var bushNoise = noise.Decoration(worldX * 0.1, 0, worldZ * 0.1);
            if (bushNoise > 0 && bushNoise < 0.05) // 5% chance for bush
                chunk.SetBlock(pos + (0, 1, 0), deadbush);

            var cactusNoise = noise.Decoration(worldX * 0.1, 1, worldZ * 0.1);
            if (cactusNoise > 0 && cactusNoise < 0.01) // 1% chance for cactus
            {
                chunk.SetBlock(pos + (0, 1, 0), cactus);
            }


            #region Trees
            // Abandon hope all ye who enter here
            var oakLeaves = Registry.GetBlock(Material.OakLeaves);
            var treeNoise = noise.Decoration(worldX * 0.1, 13, worldZ * 0.1);
            var treeHeight = TreeHeight(treeNoise);
            if (treeHeight > 0)
            {
                //Leaves
                for (int y = treeHeight + 1; y > treeHeight - 2; y--)
                {
                    for (int x = pos.X - 2; x <= pos.X + 2; x++)
                    {
                        for (int z = pos.Z - 2; z <= pos.Z + 2; z++)
                        {
                            var loc = Vector.ChunkClamped(x, y + pos.Y, z);
                            // Skip the top edges.
                            if (y == treeHeight + 1)
                            {
                                if (x != pos.X - 2 && x != pos.X + 2 && z != pos.Z - 2 && z != pos.Z + 2)
                                    chunk.SetBlock(loc, oakLeaves);
                            }
                            else
                            {
                                chunk.SetBlock(loc, oakLeaves);
                            }
                        }
                    }
                }
                for (int y = 1; y <= treeHeight; y++)
                {
                    chunk.SetBlock(pos + (0, y, 0), new Block(74));
                }
            }

            // If on the edge of the chunk, check if neighboring chunks need leaves.
            if (pos.X == 0)
            {
                // Check out to 2 blocks into the neighboring chunk's noisemap and see if there's a tree center (top log)
                var neighborTree1 = TreeHeight(noise.Decoration((worldX - 1) * 0.1, 13, worldZ * 0.1));
                var neighborTree2 = TreeHeight(noise.Decoration((worldX - 2) * 0.1, 13, worldZ * 0.1));
                var rowsToDraw = neighborTree1 > 0 ? 2 : neighborTree2 > 0 ? 1 : 0;
                var treeY = Math.Max(neighborTree1, neighborTree2) + (int)noise.Terrain(worldX - 1, worldZ);

                for (int x = 0; x < rowsToDraw; x++)
                {
                    for (int z = pos.Z - 2; z <= pos.Z + 2; z++)
                    {
                        for (int y = treeY + 1; y > treeY - 2; y--)
                        {
                            var loc = Vector.ChunkClamped(x, y + pos.Y, z);
                            // Skip the top edges.
                            if (y == treeY + 1)
                            {
                                if (x != rowsToDraw - 1 && z != pos.Z - 2 && z != pos.Z + 2)
                                    chunk.SetBlock(loc, oakLeaves);
                            }
                            else
                            {
                                chunk.SetBlock(loc, oakLeaves);
                            }
                        }
                    }
                }
            }
            else if (pos.X == 15)
            {
                // Check out to 2 blocks into the neighboring chunk's noisemap and see if there's a tree center (top log)
                var neighborTree1 = TreeHeight(noise.Decoration((worldX + 1) * 0.1, 13, worldZ * 0.1));
                var neighborTree2 = TreeHeight(noise.Decoration((worldX + 2) * 0.1, 13, worldZ * 0.1));
                var rowsToDraw = neighborTree1 > 0 ? 2 : neighborTree2 > 0 ? 1 : 0;
                var treeY = Math.Max(neighborTree1, neighborTree2) + (int)noise.Terrain(worldX + 1, worldZ);

                for (int x = 15; x > 15 - rowsToDraw; x--)
                {
                    for (int z = pos.Z - 2; z <= pos.Z + 2; z++)
                    {
                        for (int y = treeY + 1; y > treeY - 2; y--)
                        {
                            var loc = Vector.ChunkClamped(x, y + pos.Y, z);
                            // Skip the top edges.
                            if (y == treeY + 1)
                            {
                                if (x != 16 - rowsToDraw && z != pos.Z - 2 && z != pos.Z + 2)
                                    chunk.SetBlock(loc, oakLeaves);
                            }
                            else
                            {
                                chunk.SetBlock(loc, oakLeaves);
                            }
                        }
                    }
                }
            }
            if (pos.Z == 0)
            {
                // Check out to 2 blocks into the neighboring chunk's noisemap and see if there's a tree center (top log)
                var neighborTree1 = TreeHeight(noise.Decoration(worldX * 0.1, 13, (worldZ - 1) * 0.1));
                var neighborTree2 = TreeHeight(noise.Decoration(worldX * 0.1, 13, (worldZ - 2) * 0.1));
                var rowsToDraw = neighborTree1 > 0 ? 2 : neighborTree2 > 0 ? 1 : 0;
                var treeY = Math.Max(neighborTree1, neighborTree2) + (int)noise.Terrain(worldX, worldZ - 1);

                for (int z = 0; z < rowsToDraw; z++)
                {
                    for (int x = pos.X - 2; x <= pos.X + 2; x++)
                    {
                        for (int y = treeY + 1; y > treeY - 2; y--)
                        {
                            var loc = Vector.ChunkClamped(x, y + pos.Y, z);
                            // Skip the top edges.
                            if (y == treeY + 1)
                            {
                                if (x != pos.X - 2 && x != pos.X + 2 && z != rowsToDraw - 1)
                                    chunk.SetBlock(loc, oakLeaves);
                            }
                            else
                            {
                                chunk.SetBlock(loc, oakLeaves);
                            }
                        }
                    }
                }
            }
            else if (pos.Z == 15)
            {
                // Check out to 2 blocks into the neighboring chunk's noisemap and see if there's a tree center (top log)
                var neighborTree1 = TreeHeight(noise.Decoration(worldX * 0.1, 13, (worldZ + 1) * 0.1));
                var neighborTree2 = TreeHeight(noise.Decoration(worldX * 0.1, 13, (worldZ + 2) * 0.1));
                var rowsToDraw = neighborTree1 > 0 ? 2 : neighborTree2 > 0 ? 1 : 0;
                var treeY = Math.Max(neighborTree1, neighborTree2) + (int)noise.Terrain(worldX, worldZ + 1);

                for (int z = 15; z > 15 - rowsToDraw; z--)
                {
                    for (int x = pos.X - 2; x <= pos.X + 2; x++)
                    {
                        for (int y = treeY + 1; y > treeY - 2; y--)
                        {
                            var loc = Vector.ChunkClamped(x, y + pos.Y, z);
                            // Skip the top edges.
                            if (y == treeY + 1)
                            {
                                if (x != pos.X - 2 && x != pos.X + 2 && z != 16 - rowsToDraw)
                                    chunk.SetBlock(loc, oakLeaves);
                            }
                            else
                            {
                                chunk.SetBlock(loc, oakLeaves);
                            }
                        }
                    }
                }
            }
            #endregion
        }

        private static int TreeHeight(double value)
        {
            return value > 0.04 && value <= 0.05 ? (int)(value * 100) : 0;
        }
    }
}
