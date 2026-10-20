// Crc16.cs - 16-bit CCITT CRC variants.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TrajectoryLogTools.Core
{
    public static class Crc16
    {
        public sealed class Variant
        {
            public readonly string Name;
            readonly bool _reflected;
            readonly ushort _xorOut;
            readonly ushort[] _table = new ushort[256];

            public Variant(string name, bool reflected, ushort xorOut)
            {
                Name = name; _reflected = reflected; _xorOut = xorOut;
                for (int i = 0; i < 256; i++)
                {
                    ushort c;
                    if (reflected)
                    {
                        c = (ushort)i;
                        for (int k = 0; k < 8; k++) c = (ushort)((c & 1) != 0 ? (c >> 1) ^ 0x8408 : c >> 1);
                    }
                    else
                    {
                        c = (ushort)(i << 8);
                        for (int k = 0; k < 8; k++) c = (ushort)((c & 0x8000) != 0 ? (c << 1) ^ 0x1021 : c << 1);
                    }
                    _table[i] = c;
                }
            }

            public ushort Compute(byte[] data, int length)
            {
                ushort crc = 0xFFFF;
                if (_reflected)
                    for (int i = 0; i < length; i++) crc = (ushort)((crc >> 8) ^ _table[(crc ^ data[i]) & 0xFF]);
                else
                    for (int i = 0; i < length; i++) crc = (ushort)((crc << 8) ^ _table[((crc >> 8) ^ data[i]) & 0xFF]);
                return (ushort)(crc ^ _xorOut);
            }
        }

        public static readonly Variant[] Variants =
        {
            new Variant("CRC-16/CCITT-FALSE", false, 0x0000), // poly 0x1021, seed 0xFFFF, MSB-first
            new Variant("CRC-16/MCRF4XX",     true,  0x0000),
            new Variant("CRC-16/GENIBUS",     false, 0xFFFF),
            new Variant("CRC-16/X-25",        true,  0xFFFF),
        };

        public static Variant Detect(byte[] data, int length, ushort stored)
        {
            foreach (var v in Variants)
                if (v.Compute(data, length) == stored) return v;
            return null;
        }
    }
}
