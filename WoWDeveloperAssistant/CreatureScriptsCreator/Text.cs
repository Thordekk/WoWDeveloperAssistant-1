﻿using System;

namespace WoWDeveloperAssistant
{
    public class CreatureText
    {
        public string creatureText;
        public bool isAggroText;
        public bool isDeathText;
        public TimeSpan sayTime;

        public CreatureText(Packets.ChatPacket chatPacket, bool isAggroText = false, bool isDeathText = false)
        {
            creatureText = chatPacket.creatureText;
            sayTime = chatPacket.packetSendTime;
            this.isAggroText = isAggroText;
            this.isDeathText = isDeathText;
        }
    }
}
