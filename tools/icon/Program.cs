using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace GreenChrome.Tools
{
    /// <summary>
    /// 生成 GreenChrome 的应用图标：绿色渐变圆底 + 白色 Chrome 三瓣 + 绿心。
    /// 输出多尺寸 ICO（16/24/32/48/64/128/256），兼容老系统（BGRA + AND mask）。
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            var outPath = args.Length > 0 ? args[0] : @"D:\cs_project\GreenChrome\app.ico";
            var pngPath = args.Length > 1 ? args[1] : @"D:\cs_project\GreenChrome\app.png";

            int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };

            var images = new List<byte[]>();
            foreach (var s in sizes)
                images.Add(RenderBmp(s));

            WriteIco(outPath, sizes, images);

            // 同时存一张 256 的 PNG，方便预览
            using (var bmp = RenderBitmap(256))
                bmp.Save(pngPath, ImageFormat.Png);

            // ASCII 预览（把 256 缩到 48 输出）
            Console.WriteLine("=== 48x48 预览 ===");
            using (var bmp = new Bitmap(RenderBitmap(256), 48, 48))
            {
                for (int y = 0; y < 48; y++)
                {
                    var sb = new System.Text.StringBuilder();
                    for (int x = 0; x < 48; x++)
                    {
                        var c = bmp.GetPixel(x, y);
                        // 亮度 + 是否偏绿
                        double lum = (c.R * 0.299 + c.G * 0.587 + c.B * 0.114) / 255.0;
                        bool green = c.G > c.R && c.G > c.B;
                        if (lum > 0.75) sb.Append('#');
                        else if (green && lum > 0.45) sb.Append('+');
                        else if (green) sb.Append(':');
                        else if (lum > 0.2) sb.Append('.');
                        else sb.Append(' ');
                    }
                    Console.WriteLine(sb.ToString());
                }
            }

            Console.WriteLine();
            Console.WriteLine("已生成 " + outPath + "（" + sizes.Length + " 个尺寸）");
        }

        /// <summary>画图标。</summary>
        static Bitmap RenderBitmap(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                float s = size;
                float cx = s / 2f, cy = s / 2f;
                float rOuter = s * 0.47f;          // 绿底圆半径

                // 绿底：左上亮 → 右下深的线性渐变
                using (var brush = new LinearGradientBrush(
                    new PointF(cx - rOuter * 0.7f, cy - rOuter * 0.7f),
                    new PointF(cx + rOuter * 0.7f, cy + rOuter * 0.7f),
                    Color.FromArgb(255, 74, 222, 128),   // #4ADE80
                    Color.FromArgb(255, 21, 128, 61)))  // #15803D
                {
                    g.FillEllipse(brush, cx - rOuter, cy - rOuter, rOuter * 2, rOuter * 2);
                }

                // 白色三瓣（Chrome 标志造型）
                float rChrome = s * 0.30f;   // 外半径
                float rHole = s * 0.113f;    // 中心圆半径
                using (var white = new SolidBrush(Color.White))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float a0 = -90f + i * 120f;
                        var path = new GraphicsPath();
                        var rectIn = new RectangleF(cx - rHole, cy - rHole, rHole * 2, rHole * 2);
                        var rectOut = new RectangleF(cx - rChrome, cy - rChrome, rChrome * 2, rChrome * 2);
                        path.AddArc(rectIn, a0, 120f);          // 内弧
                        path.AddArc(rectOut, a0 + 120f, -120f);  // 外弧（自动连线）
                        path.CloseFigure();
                        g.FillPath(white, path);
                        path.Dispose();
                    }
                }

                // 中心绿心（把三瓣围住的中心挖成背景色）
                using (var brush = new SolidBrush(Color.FromArgb(255, 26, 178, 92)))
                {
                    g.FillEllipse(brush, cx - rHole, cy - rHole, rHole * 2, rHole * 2);
                }
            }
            return bmp;
        }

        /// <summary>把位图转成 ICO 用的 BGRA 数据（含 AND mask）。</summary>
        static byte[] RenderBmp(int size)
        {
            using (var bmp = RenderBitmap(size))
            {
                var data = bmp.LockBits(new Rectangle(0, 0, size, size),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                int stride = data.Stride;
                int rowBytes = size * 4;
                var pixels = new byte[rowBytes * size];
                for (int y = 0; y < size; y++)
                    System.Runtime.InteropServices.Marshal.Copy(
                        data.Scan0 + y * stride, pixels, y * rowBytes, rowBytes);
                bmp.UnlockBits(data);

                // 上下颠倒（ICO 的 BMP 是底向上）
                var flipped = new byte[pixels.Length];
                for (int y = 0; y < size; y++)
                    Array.Copy(pixels, (size - 1 - y) * rowBytes, flipped, y * rowBytes, rowBytes);

                // BGRA
                for (int i = 0; i < flipped.Length; i += 4)
                {
                    byte b = flipped[i], g = flipped[i + 1], r = flipped[i + 2], a = flipped[i + 3];
                    flipped[i] = b; flipped[i + 1] = g; flipped[i + 2] = r; flipped[i + 3] = a;
                }

                // AND mask：1bpp，4 字节对齐。全部 0（靠 alpha 表示透明）
                int maskStride = ((size + 31) / 32) * 4;
                var mask = new byte[maskStride * size];

                using (var ms = new MemoryStream())
                {
                    var bw = new BinaryWriter(ms);
                    bw.Write(40);                       // biSize
                    bw.Write(size);                    // biWidth
                    bw.Write(size * 2);                // biHeight（图像+掩码）
                    bw.Write((short)1);                 // biPlanes
                    bw.Write((short)32);                // biBitCount
                    bw.Write(0);                       // biCompression = BI_RGB
                    bw.Write(flipped.Length + mask.Length); // biSizeImage
                    bw.Write(0); bw.Write(0);          // 实际分辨率
                    bw.Write(0); bw.Write(0);          // 调色板
                    bw.Write(flipped);
                    bw.Write(mask);
                    bw.Flush();
                    return ms.ToArray();
                }
            }
        }

        /// <summary>
        /// 写 ICO 文件（ICONDIR + 多个 ICONDIRENTRY）。
        /// 256x256 用 PNG 格式嵌入（Vista+ 支持），其余用传统 BGRA，
        /// 既省体积又保证老系统能显示。
        /// </summary>
        static void WriteIco(string path, int[] sizes, List<byte[]> images)
        {
            // 256 的换成 PNG
            var payloads = new List<byte[]>(images);
            for (int i = 0; i < sizes.Length; i++)
            {
                if (sizes[i] != 256) continue;
                using (var bmp = RenderBitmap(256))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    payloads[i] = ms.ToArray();
                }
            }

            using (var fs = File.Create(path))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write((short)0);      // reserved
                bw.Write((short)1);      // type = icon
                bw.Write((short)sizes.Length);

                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));  // width
                    bw.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));  // height
                    bw.Write((byte)0);      // 调色板数
                    bw.Write((byte)0);      // 保留
                    bw.Write((short)1);      // 色彩平面
                    bw.Write((short)32);     // 位深
                    bw.Write(payloads[i].Length);
                    bw.Write(offset);
                    offset += payloads[i].Length;
                }
                foreach (var img in payloads)
                    bw.Write(img);
                bw.Flush();
            }
        }
    }
}
