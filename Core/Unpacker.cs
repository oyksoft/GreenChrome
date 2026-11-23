using System;
using System.Collections.Generic;
using System.IO;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace GreenChrome.Core
{
    /// <summary>7z 解包。基于 SharpCompress（纯托管，可编入 exe）。</summary>
    public static class Unpacker
    {
        /// <summary>
        /// 从 7z 文件中提取指定条目到目标目录。
        /// filter 判定哪些条目要提取（传 null = 全部）。
        /// 返回实际提取的文件数。
        /// </summary>
        public static int Extract(string sevenZipPath, string destDir,
                                 Func<string, bool> filter,
                                 Action<string> onFile)
        {
            if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
            int count = 0;

            using (var archive = SevenZipArchive.Open(sevenZipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;

                    // 条目路径统一用 / 分隔
                    var rel = entry.Key.Replace('\\', '/');
                    if (filter != null && !filter(rel)) continue;

                    // 防路径穿越：不允许 .. 与绝对路径
                    if (rel.Contains("..") || rel.StartsWith("/") || rel.Contains(":"))
                        throw new InvalidDataException("压缩包内包含非法路径: " + rel);

                    var target = Path.Combine(destDir, rel.Replace('/', Path.DirectorySeparatorChar));
                    var dir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    entry.WriteToFile(target, new ExtractionOptions { Overwrite = true });
                    count++;
                    if (onFile != null) onFile(rel);
                }
            }
            return count;
        }

        /// <summary>
        /// 直接从流的指定区间当作 7z 读取并提取（用于 PE 资源里的 CHROME.7Z，
        /// 省去把中间数据落盘）。
        /// </summary>
        public static int ExtractFromStream(Stream source, long offset, long length, string destDir,
                                           Func<string, bool> filter,
                                           Action<string> onFile)
        {
            if (!Directory.Exists(destDir)) Directory.CreateDirectory(destDir);
            int count = 0;

            // 包一层长度受限的流，只暴露 [offset, offset+length)
            using (var sub = new BoundedStream(source, offset, length))
            using (var archive = SevenZipArchive.Open(sub, new ReaderOptions { LeaveStreamOpen = true }))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;

                    var rel = entry.Key.Replace('\\', '/');
                    if (filter != null && !filter(rel)) continue;
                    if (rel.Contains("..") || rel.StartsWith("/") || rel.Contains(":"))
                        throw new InvalidDataException("压缩包内包含非法路径: " + rel);

                    var target = Path.Combine(destDir, rel.Replace('/', Path.DirectorySeparatorChar));
                    var dir = Path.GetDirectoryName(target);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    entry.WriteToFile(target, new ExtractionOptions { Overwrite = true });
                    count++;
                    if (onFile != null) onFile(rel);
                }
            }
            return count;
        }

        /// <summary>只读取 7z 内的条目清单（不解压），用于先看看包里有什么。</summary>
        public static List<string> ListEntries(string sevenZipPath)
        {
            var list = new List<string>();
            using (var archive = SevenZipArchive.Open(sevenZipPath))
            {
                foreach (var entry in archive.Entries)
                    list.Add(entry.Key);
            }
            return list;
        }

        /// <summary>限制长度的流视图：只暴露 [start, start+length) 区段。</summary>
        private class BoundedStream : Stream
        {
            private readonly Stream _inner;
            private readonly long _start;
            private readonly long _length;
            private long _pos;

            public BoundedStream(Stream inner, long start, long length)
            {
                _inner = inner;
                _start = start;
                _length = length;
            }

            private long Remaining { get { return _length - _pos; } }

            public override bool CanRead { get { return true; } }
            // 7z 需要随机访问（header 在文件尾），必须支持 Seek
            public override bool CanSeek { get { return true; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { return _length; } }

            public override long Position
            {
                get { return _pos; }
                set { _pos = value < 0 ? 0 : (value > _length ? _length : value); }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (Remaining <= 0) return 0;
                if (count > Remaining) count = (int)Remaining;

                _inner.Position = _start + _pos;
                int n = 0;
                while (n < count)
                {
                    int r = _inner.Read(buffer, offset + n, count - n);
                    if (r <= 0) break;
                    n += r;
                }
                if (n <= 0) return 0;
                _pos += n;
                return n;
            }

            public override void Flush() { }

            public override long Seek(long offset, SeekOrigin origin)
            {
                long target;
                switch (origin)
                {
                    case SeekOrigin.Begin: target = offset; break;
                    case SeekOrigin.Current: target = _pos + offset; break;
                    default: target = _length + offset; break;
                }
                if (target < 0) target = 0;
                if (target > _length) target = _length;
                _pos = target;
                return _pos;
            }
            public override void SetLength(long value) { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }
    }
}
