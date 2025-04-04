﻿using Arrowgene.Buffers;
using Arrowgene.DJMaxOnline.Server;

namespace ConsoleApp1;

public class DjMaxCryptoBak
{
    public static void Main(string[] args)
    {
        DjMaxCryptoBak c = new DjMaxCryptoBak();
        c.Decrypt();
    }
    
    public void Shad(byte[] input, uint crc32)
    {
        byte[] crc = BitConverter.GetBytes(crc32);
        for (int i = 0; i < input.Length; i++)
        {
            byte dl = input[i];
            dl = (byte)~dl;
            dl = (byte)(dl + crc[i % crc.Length]);

            int s = 1;
        }

    }


    public DjMaxCryptoBak()
    {
    }

    public void Decrypt()
    {
        // client side

        // pre connect
        byte[] seed = new byte[(0xFA * 4) + 12];
        IBuffer bSnd = new StreamBuffer(seed);
        IBuffer bRcv = new StreamBuffer(seed);
        sub_49F554(bSnd);
        sub_49F554(bRcv);


        // recv on connect ack
        byte[] onConAckBuf = new byte[]
        {
            0x09, 0x00, 0xCC, 0x05, 0x00, 0x4D, 0x01,
            0x9A, 0xDF, 0xE5, 0x67,
            0x11, 0x57, 0xB2, 0xFE,
            0xE8, 0x07, 0x06, 0x00, 0x10, 0x00, 0x00, 0x00, 0x25, 0x00, 0x2C, 0x00, 0xC0, 0x09, 0x35, 0x38, 0x0B, 0x3E,
            0xB4, 0xA2, 0xB0, 0x11, 0x00, 0x00,
            0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };
        IBuffer sOnConAckBuf = new StreamBuffer(onConAckBuf);
        sOnConAckBuf.Position = 7;
        uint a = sOnConAckBuf.ReadUInt32();
        uint b = sOnConAckBuf.ReadUInt32();
        a = ~a;
        b = ~b;
        sOnConAckBuf.Position = 7;
        sOnConAckBuf.WriteUInt32(a);
        sOnConAckBuf.WriteUInt32(b);
        //09 00 CC 05 00 4D 01
        //9A DF E5 67 -> NOT -> 98 1A 20 65
        //11 57 B2 FE -> NOT -> 01 4D A8 EE
        //E8 07 06 00 10 00 00 00 25 00 2C 00 C0 09 35 38 0B 3E B4 A2 B0 11 00 00
        //CC CC CC CC CC CC CC CC 00 00 00 00 00 00 00
        byte[] r1 = sOnConAckBuf.GetBytes(7, 32);
        IBuffer bR1 = new StreamBuffer(r1);
        int s1 = sub_4AF070(r1, 32); // 0x807
        byte ba2 = (byte)s1; // 0x07
        //65 20 1A 98 -- seed mt
        //EE A8 4D 01 -- seed mt
        //E8 07 06 00 10 00 00 00 25 00 2C 00 C0 09 35 38 0B 3E B4 A2 B0 11 00 00
        //07

        uint crc32 = Crc32.GetHash(r1);
        //Shad(,crc32);
        //83ACA9C9

        ushort s2 = sOnConAckBuf.GetUInt16(5); // 0x4D 0x01
        uint u1 = bR1.GetUInt32(28); // 0xB0 0x11 0x00 0x00

        sub_49F563(bSnd, u1);
        sub_49F563(bRcv, u1);

        MersenneTwister mt = new MersenneTwister(r1);

        encrypt(r1, u1);

        // Missing send encryption
        sub_49F4A2(bSnd);
        // TODO encrypt implementation

        // recv XX
        sub_49F4A2(bRcv);
        sub_49F4A2(bRcv);
        byte[] onXX = new byte[]
        {
            0x10, 0x00, 0x1C, 0xF9, 0x05, 0x00, 0x00,
            0xE2, 0x9F, 0xF5, 0xEE, 0x30, 0x70, 0x3C, 0x52, 0xC2, 0x99, 0x80, 0xEB, 0x70, 0x68, 0x44, 0xFB,
            0x87, 0x57, 0x75, 0x25, 0xF1, 0x07, 0xE7, 0xC4, 0xE3, 0x07, 0x6C, 0xC1, 0x4B, 0x1B, 0xB8, 0xE4,
            0x84, 0xD9, 0x76, 0x9C, 0xDE, 0x24, 0x9C, 0xBC, 0xC0, 0xEE, 0xD8, 0xF0, 0xDD, 0x4E, 0x13, 0x1B,
            0x7D, 0x34, 0xC1, 0x9E, 0x34, 0x57, 0x84, 0xC3, 0x92, 0xD8, 0xEB, 0x03, 0xAA, 0x05, 0x75, 0x3A,
            0x24, 0x94, 0x54, 0xFB, 0x60, 0xDF, 0x75, 0xCE, 0x95, 0xA3, 
            0xFA, 0x07, 0xAE, 0x64, 0x27, 0x6F,
            0xAA, 0x69, 0xBF, 0x35, 0x24, 
            //0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };

        byte[] onXX1 = new byte[]
        {
           // 0x2f, 0x00, 0x61, 0xf9, 0x05, 0x00, 0x00, 
            0xd1, 0xbb, 0x92, 0x1f, 0xca, 0x85, 0x4c, 0xd3, 0xcf, 0xce, 0xb8, 0x44, 
            //
            //0xd3, 0x00, 0xd7, 0x00, 0x00, 0xe8, 0x07,
            0x21, 0x7d, 0x96, 0x14, 0x90, 0x9b, 0x92, 0x45, 0x3a, 0xc4, 0x4b, 0x49, 
            0x1d, 0x93, 0xc3, 0x6a, 0xa9, 0x43, 0x1c, 0xf2, 0x8d, 0xa9, 0xa7, 0x0f, 
            0xf3, 0x6b,
            //
            //0x0b, 0x00, 0x48, 0xbb, 0x00, 0x00, 0x00, 
            0x80, 0x50, 0x47, 0x3f, 0xae,
            0xc9, 0x27, 0x1e, 0x0c, 0x22, 0x94, 0x0b, 0x09, 0xa0, 0x91, 0x80, 0x4d, 0x42, 0x19, 0xf3, 0x48,
            0x7a, 0x7a, 0x12, 0xae, 0xfe, 0x2c, 0x16, 0x44, 0xbf, 0xb9, 0x11, 0x3e, 0x14, 0x4c, 0xf1, 0x69,
            0x75, 0xfd, 0x06, 0x8f, 0x6f, 0x09, 0x95, 0x0e, 0x45, 0x68, 0xf1, 0x4b, 0xb5, 0x70, 0xa7, 0x83,
            0xeb, 0xfc, 0x9e, 0xf7, 0x80, 0x0c, 0x0e, 0x1a, 0xd4, 0x7b, 0x51, 0x0a, 0xf8, 0x5a, 0xda, 0x55, 0xe6, 0xc0,
            0x1b, 0x87, 0x2c, 0x26, 0xc4, 0xbb, 0x75, 0x33, 0x55, 0x01, 0xec, 0x7e,
            0x2e, 0x78, 0x28, 0xd5, 0xd0, 0x4d, 0xe7, 0xe1, 0x43, 0x6d, 0xb4, 0xec, 0xa2, 0x4d, 0x5e, 0x10, 0xb4, 0xf8,
            0x57, 0xf5, 0x54, 0x56, 0x76, 0x5d, 0x57, 0x2c, 0x34, 0x5b, 0x59, 0x39, 0x0f, 0x2d, 0x56, 0x9c, 0x8d, 0xb0,
            0x1e, 0x92, 0x96, 0x38, 0xa8, 0x7f, 0x85, 0x3a, 0xb7, 0x88, 0xde, 0x37, 0xfa, 0x56, 0xa0, 0x46, 0xc2, 0x02,
            0xd0, 0x1a, 0x4c, 0x84, 0x44, 0x4f, 0xb6, 0xe9, 0x74, 0x6f, 0xb0, 0xe0, 0xa1, 0xdb, 0x3b, 0xe4, 0x2b, 0xe6,
            0xfd, 0x52, 0xd9, 0x55, 0xba, 0x1c, 0x7e, 0xfe, 0x43, 0x0d, 0x07, 0x17, 0xde, 0xa8, 0x77, 0x0b, 0xfe, 0x66,
            0x57, 0x1b, 0xa1, 0xa8, 0x93, 0x99, 0xe3
        };

        uint uint1 = mt.NextUInt32();
        uint uint2 = mt.NextUInt32();


        IBuffer seedBuff = new StreamBuffer();
        seedBuff.WriteUInt32(u1);
        //seedBuff.WriteUInt32(u1);
        seedBuff.WriteUInt32(0);
        int seedIdx = 0;

        IBuffer rngBuff = new StreamBuffer();
        rngBuff.WriteUInt32(uint1);
        rngBuff.WriteUInt32(uint2);
        int rngIdx = 0;


        int onXXoffset = 0;

        uint u2 = 0;

        StreamBuffer bXX = new StreamBuffer();
        bXX.WriteBytes(onXX);
        bXX.WriteBytes(onXX1);
        onXX = bXX.GetAllBytes();


        for (int i = 7; i < onXX.Length; i++)
        {
            if (seedIdx > 7)
            {
                // TODO sub_49F571
                uint edi = 0;
                uint eax = 0; //  delta sum
                uint esi = 0;
                uint edx = u2;
                uint ecx = u1;


                IBuffer onXXB = new StreamBuffer(onXX);
                uint ebp_10 = onXXB.GetUInt32(i - 4);
                uint ebp_4 = onXXB.GetUInt32(i - 8);
                uint ebp_8 = onXXB.GetUInt32(i - 4);
                uint ebp_c = onXXB.GetUInt32(i - 8);

                IBuffer clear = new StreamBuffer();
                clear.WriteUInt32(ebp_4);
                clear.WriteUInt32(ebp_10);
                Console.WriteLine("Clear:" + BitConverter.ToString(clear.GetAllBytes()).Replace("-", " "));

               // Console.WriteLine($"BAK. i:{i}");
               // Console.WriteLine($"EBP10:{ebp_10}");
               // Console.WriteLine($"ebp_4:{ebp_4}");
               // Console.WriteLine($"edx:{edx}");
               // Console.WriteLine($"ecx:{ecx}");
               // Console.WriteLine($"---------");


                for (int i1 = 0; i1 < 32; i1++)
                {
                    eax = edx;
                    eax = eax >> 5;
                    eax = eax + ebp_10; // ebp+10 //07 00 00 00
                    edi = edx;
                    edi = edi << 4;
                    edi = edi + ebp_4; // ebp-4 // AB 64 AB C8
                    esi = esi - 0x61C88647;
                    eax = eax ^ edi;
                    edi = esi + edx; // [esi + edx]
                    eax = eax ^ edi;
                    ecx = ecx + eax;

                    eax = ecx;
                    eax = eax >> 5;
                    eax = eax + ebp_8; // ebp-8
                    edi = ecx;
                    edi = edi << 4;
                    edi = edi + ebp_c; // ebp-C
                    eax = eax ^ edi;
                    edi = esi + ecx; //
                    eax = eax ^ edi;
                    edx = edx + eax;
                }

                //  ecx  = B0 11 XX XX
                //  edx  = B0 11 YY YY   XX XX XX XX
                seedBuff = new StreamBuffer();
                seedBuff.WriteUInt32(ecx);
                //seedBuff.WriteUInt32(u1);
                seedBuff.WriteUInt32(edx);
                seedIdx = 0;
                uint1 = mt.NextUInt32();
                uint2 = mt.NextUInt32();
                rngBuff = new StreamBuffer();
                rngBuff.WriteUInt32(uint1);
                rngBuff.WriteUInt32(uint2);
                u1 = ecx;
                u2 = edx;
                rngIdx = 0;
            }

            byte x_seed = seedBuff.GetByte(seedIdx);
            byte x_rng = rngBuff.GetByte(rngIdx);
            byte x_key = (byte)(x_seed ^ x_rng);
            byte encrpyted = onXX[i];
            byte decrypted = (byte)(encrpyted ^ x_key);
            onXX[i] = decrypted;
         //   seedBuff.Position = seedIdx;
         //   seedBuff.WriteByte(decrypted);
            seedIdx++;
            rngIdx++;

            int asddf = 1;
        }

        Console.WriteLine("DECRYPTED");
        Console.WriteLine(Util.HexDump(onXX));
        int asd = 1;
    }

    //private void decipher(uint[] e_block) {
    //    int delta_sum = _iterationSpec._deltaSumInitial;
    //    int n = _iterationSpec._iterations;
    //    while (n-- > 0) {
    //        e_block[1] -= ((e_block[0] << 4 ^ e_block[0] >> 5) + e_block[0])
    //                      ^ (delta_sum + _key[delta_sum >> 11 & 3]);
    //        delta_sum -= DELTA;
    //        e_block[0] -= ((e_block[1] << 4 ^ e_block[1] >> 5) + e_block[1])
    //                      ^ (delta_sum + _key[delta_sum & 3]);
    //    }
    //}

    private void encrypt(byte[] mt_seed, uint delta_seed)
    {
        DjMaxCrypto crypto = new DjMaxCrypto(mt_seed, delta_seed);

        byte[] data = new byte[]
        {
            0xAB, 0x54, 0xAB, 0xC8, 0xC8, 0xC8, 0x0E, 0xA8, 0x38, 0x43, 0xA8, 0x82, 0x82, 0xC8, 0xA8, 0xAB,
            0xC8, 0x0E, 0xAB, 0x54, 0x43, 0x38, 0x82, 0xA8, 0xC8, 0xA8, 0xC8, 0xAB, 0x65, 0x20, 0x1A, 0x98,
            0xEE, 0xA8, 0x4D, 0x01, 0xE8, 0x07, 0x06, 0x00, 0x10, 0x00, 0x00, 0x00, 0x25, 0x00, 0x2C, 0x00,
            0xC0, 0x09, 0x35, 0x38, 0x0B, 0x3E, 0xB4, 0xA2, 0xB0, 0x11, 0x00, 0x00, 0xAB, 0xAB, 0xAB, 0xAB,
            0xAB, 0xAB, 0xAB, 0xAB, //0xFE, 0xEE, 0xFE, 0xEE, 0xFE, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            // 0x00, 0xD9, 0x53, 0xF4, 0x61, 0x0C, 0xB1, 0x13, 0x00, 0xE8, 0x03, 0x56, 0x07, 0x88, 0x24, 0x56
        };
        Span<byte> t = data;

        crypto.Encrypt(ref t);

        Console.WriteLine("encCCCCCCCCCCCC");
        Console.WriteLine(Util.HexDump(data));

        byte[] onXX = new byte[]
        {
            // 0x10, 0x00, 0x1C, 0xF9, 0x05, 0x00, 0x00,
            0xE2, 0x9F, 0xF5, 0xEE, 0x30, 0x70, 0x3C, 0x52, 0xC2, 0x99, 0x80, 0xEB, 0x70, 0x68, 0x44, 0xFB,
            0x87, 0x57, 0x75, 0x25, 0xF1, 0x07, 0xE7, 0xC4, 0xE3, 0x07, 0x6C, 0xC1, 0x4B, 0x1B, 0xB8, 0xE4,
            0x84, 0xD9, 0x76, 0x9C, 0xDE, 0x24, 0x9C, 0xBC, 0xC0, 0xEE, 0xD8, 0xF0, 0xDD, 0x4E, 0x13, 0x1B,
            0x7D, 0x34, 0xC1, 0x9E, 0x34, 0x57, 0x84, 0xC3, 0x92, 0xD8, 0xEB, 0x03, 0xAA, 0x05, 0x75, 0x3A,
            0x24, 0x94, 0x54, 0xFB, 0x60, 0xDF, 0x75, 0xCE, 0x95, 0xA3, 0xFA, 0x07, 0xAE, 0x64, 0x27, 0x6F,
            0xAA, 0x69, 0xBF, 0x35, 0x24, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };

        Span<byte> t1 = onXX;
        //crypto.Decrypt(ref  t1);

        Util.HexDump(onXX);


        int a = 123;
    }


    private void sub_49F563(IBuffer b, uint u1)
    {
        b.Position = 0;
        b.WriteUInt32(u1);
        sub_49F50E(b);
    }

    private int sub_4AF070(byte[] buf, int len)
    {
        int result = 0;
        for (int i = 0; i < len; i++)
        {
            result += buf[i];
        }

        return result;
    }

    private void sub_49F554(IBuffer b)
    {
        b.Position = 0;
        b.WriteUInt32(0);
        sub_49F50E(b);
    }

    private void sub_49F50E(IBuffer b)
    {
        b.Position = 4;
        b.WriteUInt32(0);
        b.WriteUInt32(0x67);
        b.Position = 0;
        for (int i = 0xFA; i > 0; i--)
        {
            uint r = sub_49F4D9(b, 0);
            b.Position = ((i * 4) - 4) + 12;
            b.WriteUInt32(r);
        }

        uint v4 = 0xFFFFFFFF;
        uint v5 = 0x80000000;
        int pos = 0x18;
        while (v5 != 0)
        {
            b.Position = pos;
            uint result = b.ReadUInt32();
            result = result & v4;
            result = result | v5;
            b.Position = pos;
            b.WriteUInt32(result);
            v5 >>= 1;
            v4 >>= 1;
            pos += 0x1C;
        }
    }

    private uint sub_4C2E70(IBuffer b, int len)
    {
        uint eax = 0xFFFFFFFF;
        uint ecx = 0;
        for (int i = 0; i < len; i++)
        {
            uint edi = b.ReadByte();
            uint ebx = eax;
            ebx = ebx & 0xFF;
            edi = edi ^ ebx;
        }

        return 0;
    }

    private uint sub_49F4D9(IBuffer b, int pos)
    {
        b.Position = pos;
        uint val = b.ReadUInt32();
        for (int i = 0; i < 32; i++)
        {
            uint u1 = val >> 2;
            uint u2 = u1 ^ val;
            uint u3 = u2 >> 3;
            uint x1 = val;
            uint x2 = x1 >> 1;
            uint s3 = u3 ^ x2;
            uint s4 = s3 ^ val;
            uint s5 = s4 >> 1;
            uint s6 = s5 ^ val;
            uint s7 = s6 >> 1;
            uint s9 = s7 ^ x2;
            uint s10 = s9 ^ val;
            uint w = val << 0x1F;
            uint v = s10 & 0x1;
            uint y = w | x2;
            uint z = y ^ v;
            val = z;
        }

        b.Position = pos;
        b.WriteUInt32(val);
        return val;
    }

    public uint sub_49F4A2(IBuffer b)
    {
        uint idx1 = b.GetUInt32(4);
        uint idx2 = b.GetUInt32(8);
        uint idx1a = (idx1 + 3) * 4;
        uint idx2a = (idx2 + 3) * 4;

        uint v1 = b.GetUInt32((int)idx1a);
        uint v2 = b.GetUInt32((int)idx2a);
        uint x = v1 ^ v2;
        b.Position = (int)idx1a;
        b.WriteUInt32(x);


        uint result = b.GetUInt32((int)idx1a);
        idx1++;
        if (idx1 == 0xFA)
        {
            idx1 = 0;
        }

        b.Position = 4;
        b.WriteUInt32(idx1);

        idx2++;
        if (idx2 == 0xFA)
        {
            idx2 = 0;
        }

        b.Position = 8;
        b.WriteUInt32(idx2);
        return result;
    }
}