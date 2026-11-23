using System;
using System.IO;

namespace GreenChrome.Core
{
    /// <summary>
    /// PE 资源提取：从 Chrome 离线安装包 exe 中取出内嵌的 7z 归档。
    ///
    /// 离线包结构：PE 资源表里有类型 "BN"、名字 "CHROME.7Z" 的资源，
    /// 它是一段完整的 7z 归档数据（在文件中的某个 offset 处）。
    /// 这里解析 PE 头，定位该资源，输出 (offset, size)，
    /// 上层用 FileStream + Seek 直接把它当 7z 流解开，无需把中间数据再落盘。
    /// </summary>
    public static class PeResource
    {
        public class ResourceLocation
        {
            public long Offset;   // 在 exe 文件中的字节偏移
            public long Size;     // 资源数据长度
            public string Type;   // 资源类型（如 BN）
            public string Name;   // 资源名（如 CHROME.7Z）
        }

        /// <summary>
        /// 查找 exe 中第一个匹配 type/name 的资源。
        /// 找不到返回 null。
        /// </summary>
        public static ResourceLocation Find(string exePath, string type, string name)
        {
            using (var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                // ---- DOS 头 ----
                if (br.ReadUInt16() != 0x5A4D) throw new InvalidDataException("不是有效的 PE 文件（缺少 MZ）");
                fs.Position = 0x3C;
                int peOffset = br.ReadInt32();
                fs.Position = peOffset;
                if (br.ReadUInt32() != 0x00004550) throw new InvalidDataException("不是有效的 PE 文件（缺少 PE 签名）");

                // ---- COFF 头 ----
                br.ReadUInt16();                       // Machine
                ushort numSections = br.ReadUInt16();
                br.ReadUInt32();                       // TimeDateStamp
                br.ReadUInt32();                       // PointerToSymbolTable
                br.ReadUInt32();                       // NumberOfSymbols
                ushort optHeaderSize = br.ReadUInt16();
                br.ReadUInt16();                       // Characteristics

                // ---- 可选头 ----
                int optStart = peOffset + 24;
                fs.Position = optStart;
                ushort magic = br.ReadUInt16();
                bool isPE32Plus = magic == 0x20B;

                // 数据目录：PE32 在可选头+96 处，PE32+ 在+112 处；资源目录是第 2 项
                fs.Position = optStart + (isPE32Plus ? 112 : 96) + 8 * 2;
                uint resRva = br.ReadUInt32();
                uint resSize = br.ReadUInt32();
                if (resRva == 0) return null;

                // ---- 节表（用于 RVA → 文件偏移）----
                var sections = new Section[numSections];
                fs.Position = optStart + optHeaderSize;
                for (int i = 0; i < numSections; i++)
                {
                    sections[i] = new Section();
                    br.ReadBytes(8);                       // Name
                    sections[i].VirtualSize = br.ReadUInt32();
                    sections[i].VirtualAddress = br.ReadUInt32();
                    sections[i].SizeOfRawData = br.ReadUInt32();
                    sections[i].PointerToRawData = br.ReadUInt32();
                    br.ReadUInt32();                      // PointerToRelocations
                    br.ReadUInt32();                      // PointerToLinenumbers
                    br.ReadUInt16();                      // NumberOfRelocations
                    br.ReadUInt16();                      // NumberOfLinenumbers
                    br.ReadUInt32();                      // Characteristics（节表项共 40 字节）
                }

                // 资源目录内部的所有偏移都是相对 resRva 的 VA 空间
                Func<uint, int> rvaToOffset = rva =>
                {
                    foreach (var s in sections)
                    {
                        uint size = Math.Max(s.VirtualSize, s.SizeOfRawData);
                        if (rva >= s.VirtualAddress && rva < s.VirtualAddress + size)
                            return (int)(s.PointerToRawData + (rva - s.VirtualAddress));
                    }
                    return -1;
                };

                int resDirOffset = rvaToOffset(resRva);
                if (resDirOffset < 0) return null;

                return FindInDir(br, resRva, resRva, rvaToOffset, type, name, 0);
            }
        }

        private class Section
        {
            public uint VirtualSize;
            public uint VirtualAddress;
            public uint SizeOfRawData;
            public uint PointerToRawData;
        }

        /// <summary>
        /// 在资源目录中递归查找。
        /// vaBase = 资源目录起始 VA（所有内部偏移都相对它）。
        /// level 0 = 类型目录，1 = 名字目录，2 = 语言目录。
        /// </summary>
        private static ResourceLocation FindInDir(BinaryReader br, uint vaBase, uint dirVa,
            Func<uint, int> rvaToOffset, string type, string name, int level)
        {
            int dirOffset = rvaToOffset(dirVa);
            if (dirOffset < 0) return null;

            br.BaseStream.Position = dirOffset;
            br.ReadUInt32();               // Characteristics (DWORD)
            br.ReadUInt32();               // TimeDateStamp (DWORD)
            br.ReadUInt16();               // MajorVersion (WORD!)
            br.ReadUInt16();               // MinorVersion (WORD!)
            ushort numNamed = br.ReadUInt16();
            ushort numId = br.ReadUInt16();
            int count = numNamed + numId;
            int entryPos = (int)br.BaseStream.Position;

            for (int i = 0; i < count; i++)
            {
                br.BaseStream.Position = entryPos + i * 8;
                uint idOrName = br.ReadUInt32();
                uint offsetToData = br.ReadUInt32();

                bool isName = (idOrName & 0x80000000) != 0;
                string entryName = null;
                if (isName)
                {
                    // 名字：UTF-16，长度在 (vaBase + nameOffset) 处
                    uint nameVa = vaBase + (idOrName & 0x7FFFFFFF);
                    int nameOffset = rvaToOffset(nameVa);
                    if (nameOffset < 0) continue;
                    long save = br.BaseStream.Position;
                    br.BaseStream.Position = nameOffset;
                    ushort len = br.ReadUInt16();
                    entryName = System.Text.Encoding.Unicode.GetString(br.ReadBytes(len * 2));
                    br.BaseStream.Position = save;
                }

                bool isDir = (offsetToData & 0x80000000) != 0;
                uint childVa = vaBase + (offsetToData & 0x7FFFFFFF);

                bool match;
                if (level == 0)
                {
                    if (entryName != null)
                        match = string.Equals(entryName, type, StringComparison.OrdinalIgnoreCase);
                    else
                        match = type.StartsWith("#") && type.Substring(1) == idOrName.ToString();
                }
                else if (level == 1)
                {
                    if (entryName != null)
                        match = string.Equals(entryName, name, StringComparison.OrdinalIgnoreCase);
                    else
                        match = name.StartsWith("#") && name.Substring(1) == idOrName.ToString();
                }
                else
                {
                    match = true;    // 语言层：取第一个
                }

                if (!match) continue;

                if (isDir)
                    return FindInDir(br, vaBase, childVa, rvaToOffset, type, name, level + 1);
                else
                    return ReadDataEntry(br, vaBase, childVa, rvaToOffset, type, name);
            }
            return null;
        }

        private static ResourceLocation ReadDataEntry(BinaryReader br, uint vaBase, uint entryVa,
            Func<uint, int> rvaToOffset, string type, string name)
        {
            int entryOffset = rvaToOffset(entryVa);
            if (entryOffset < 0) return null;

            br.BaseStream.Position = entryOffset;
            uint dataRva = br.ReadUInt32();
            uint size = br.ReadUInt32();
            br.ReadUInt32();  // CodePage
            br.ReadUInt32();  // Reserved

            int fileOffset = rvaToOffset(dataRva);
            if (fileOffset < 0) return null;

            return new ResourceLocation
            {
                Offset = fileOffset,
                Size = size,
                Type = type,
                Name = name
            };
        }
    }
}
