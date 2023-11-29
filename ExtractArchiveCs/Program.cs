using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace ExtractArchiveCs
{
    static class Program
    {
        const string ArchivePath = "C:\\Program Files (x86)\\phenomedia\\Die ersten 10 Jahre\\Schatz des Pharao\\mha.dat";

        static void Main(string[] args)
        {
            var reader = new BinaryReader(new FileStream(ArchivePath, FileMode.Open, FileAccess.Read));
            var archiveHeader = ArchiveHeader.Read(reader);
            reader.BaseStream.Position = archiveHeader.rootDirOffset;
            var entries = EntryHeader.ReadAll(reader);
            var entryByOffset = entries.ToDictionary(e => e.offset, e => e);
            var files = entries.Where(e => e.isFile).ToArray();

            foreach (var file in files)
            {
                var hdr = file.fileHeader!.Value;
                Console.Write(hdr.type.ToString().PadLeft(2, ' '));
                Console.Write(hdr.unk2.ToString("X").PadLeft(12, ' '));
                Console.Write(hdr.unk3.ToString("X").PadLeft(10, ' '));
                Console.Write(hdr.compressedSize.ToString().PadLeft(10, ' '));
                Console.Write(hdr.decompressedSize.ToString().PadLeft(10, ' '));
                Console.Write(hdr.offset.ToString().PadLeft(10, ' '));
                Console.Write($" \"{file.name}\" ");

                var path = new Stack<string>();
                var curParent = file.offsetParent;
                while (curParent != 0)
                {
                    path.Push(entryByOffset[curParent].name);
                    curParent = entryByOffset[curParent].offsetParent;
                }
                path.Push("out");
                Directory.CreateDirectory(string.Join('/', path));
                var filePath = string.Join('/', path) + '/' + file.name;

                reader.BaseStream.Position = file.fileHeader!.Value.offset;
                var deflate = new Ionic.Zlib.ZlibStream(reader.BaseStream, Ionic.Zlib.CompressionMode.Decompress);
                deflate.CopyTo(new FileStream(filePath, FileMode.Create, FileAccess.Write));

                Console.WriteLine("Done");
            }

            Console.WriteLine($"\"{archiveHeader.name}\" {entries.Count} entries");
        }

        private struct ArchiveHeader
        {
            public string name;
            public uint version;
            public uint archiveSize;
            public uint rootDirOffset;

            public static ArchiveHeader Read(BinaryReader reader) => new ArchiveHeader()
            {
                name =ToAsciiString(reader.ReadBytes(64)),
                version = reader.ReadUInt32(),
                archiveSize = reader.ReadUInt32(),
                rootDirOffset = reader.ReadUInt32(),
            };
        }

        private struct FileHeader
        {
            public uint type;
            public uint unk2;
            public uint unk3;
            public uint compressedSize;
            public uint decompressedSize;
            public uint offset;
        }

        private class EntryHeader
        {
            public bool isFile;
            public uint offset;
            public uint flags;
            public uint offsetPrev;
            public uint offsetNext;
            public uint offsetPrevSibling;
            public uint offsetNextSibling;
            public uint offsetParent;
            public uint offsetFirstChild;
            public FileHeader? fileHeader;

            public string name;

            public static IReadOnlyList<EntryHeader> ReadAll(BinaryReader reader)
            {
                var result = new List<EntryHeader>();
                while(true)
                {
                    var header = Read(reader);
                    if (header == null)
                        return result;
                    result.Add(header);
                }
            }

            private static EntryHeader? Read(BinaryReader reader)
            {
                var offset = reader.BaseStream.Position;
                if (reader.BaseStream.Position == reader.BaseStream.Length)
                    return null;
                var type = reader.ReadUInt32();
                if (type == 0xFFFFFFFF || type == 0xFDFDFDFD)
                    return null;
                if (type != 3 && type != 4)
                    throw new InvalidDataException("Unknown entry type");

                var r = new EntryHeader();
                r.isFile = type == 3;
                r.offset = (uint)offset;
                r.flags = reader.ReadUInt32();
                r.offsetPrev = reader.ReadUInt32();
                r.offsetNext = reader.ReadUInt32();

                var secondHeader = Decrypt(reader.ReadBytes(16));
                r.offsetPrevSibling = BitConverter.ToUInt32(secondHeader, 0);
                r.offsetNextSibling = BitConverter.ToUInt32(secondHeader, 4);
                r.offsetParent = BitConverter.ToUInt32(secondHeader, 8);
                r.offsetFirstChild = BitConverter.ToUInt32(secondHeader, 12);

                var thirdHeader = Decrypt(reader.ReadBytes(r.isFile ? 88 : 80));
                r.name = ToAsciiString(thirdHeader, 80);

                if (r.isFile)
                {
                    r.fileHeader = new FileHeader()
                    {
                        type = BitConverter.ToUInt32(thirdHeader, 64 + 4 * 0),
                        unk2 = BitConverter.ToUInt32(thirdHeader, 64 + 4 * 1),
                        unk3 = BitConverter.ToUInt32(thirdHeader, 64 + 4 * 2),
                        compressedSize = BitConverter.ToUInt32(thirdHeader, 64 + 4 * 3),
                        decompressedSize = BitConverter.ToUInt32(thirdHeader, 64 + 4 * 4),
                        offset = BitConverter.ToUInt32(thirdHeader, 64 + 4 * 5)
                    };
                }
                return r;
            }
        }

        private static byte[] Decrypt(byte[] input)
        {
            uint RotateRight(uint value, int count) => (value << (32 - count)) | (value >> count);
            uint RotateLeft(uint value, int count) => (value << count) | (value >> (32 - count));

            var buffer = input.ToArray();
            buffer[0] = (byte)~buffer[0];
            if (buffer.Length == 1)
                return buffer;

            int maxI = buffer.Length - 1;

            uint key = 0;
            uint rotMod = (uint)~maxI;
            int i = 1, j = maxI;
            do
            {
                buffer[i] = (byte)(buffer[i] ^ key);
                uint nextKey = RotateRight(key, 2 * (((int)rotMod) & 1) | 5);
                rotMod >>= 1;
                if (rotMod == 0)
                    rotMod = (uint)~maxI;
                key = nextKey + 1;
                if (key == 0)
                    key = 0x5A3C96E7;
                i++;
                j--;
            } while (j != 0);

            i = 1;
            j = maxI >> 1;
            do
            {
                byte tmp = (byte)(buffer[i] ^ 0x55);
                buffer[i] = (byte)(buffer[i + 1] ^ 0xAA);
                buffer[i + 1] = tmp;
                i += 2;
                j--;
            } while (j != 0);

            key = 0;
            rotMod = (uint)maxI;
            i = 1;
            j = maxI;
            do
            {
                buffer[i] = (byte)(buffer[i] ^ key);
                uint nextKey = RotateLeft(key, (rotMod & 1) == 0 ? 11 : 17);
                rotMod >>= 1;
                if (rotMod == 0)
                    rotMod = (uint)maxI;
                key = nextKey + 1;
                if (key == 0)
                    key = 0x5A3C96E7;
                i++;
                j--;
            } while (j != 0);

            int i1 = 1, i2 = maxI;
            j = maxI / 2;
            do
            {
                byte tmp = (byte)(buffer[i1] ^ 0xF0);
                buffer[i1] = (byte)(buffer[i2] ^ 0x0F);
                buffer[i2] = tmp;
                i1++;
                i2--;
                j--;
            } while (j != 0);

            return buffer;
        }

        private static string ToAsciiString(this byte[] bytes, int maxLength = int.MaxValue)
        {
            var length = Math.Min(Array.IndexOf(bytes, (byte)0), maxLength);
            return Encoding.ASCII.GetString(bytes, 0, length);
        }
        private static string ToHexString(this byte[] bytes) => string.Join("", bytes.Select(b => b.ToString("X2")));
    }
}
