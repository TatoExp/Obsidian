﻿using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    public partial class PlayerPosition : IServerboundPacket
    {
        [Field(0), DataFormat(typeof(double))]
        public VectorF Position { get; private set; }

        [Field(1)]
        public bool OnGround { get; private set; }

        public int Id => 0x12;

        public PlayerPosition()
        {
        }

        public PlayerPosition(VectorF position, bool onGround)
        {
            Position = position;
            OnGround = onGround;
        }

        public async ValueTask HandleAsync(Server server, Player player)
        {
            await player.UpdateAsync(server, Position, OnGround);
            await player.World.UpdateClientChunksAsync(player.client);
        }
    }
}
