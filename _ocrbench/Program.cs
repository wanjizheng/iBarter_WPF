using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.OCR;
using ImageMagick;

namespace OcrBench;

class Program
{
    record Variant(char Id, int Scale, bool Negate, int Threshold, int Psm);

    record Case(string File, int Expected);

    record Row(string File, int Expected, string Variant, int Scale, bool Negate, int Threshold, int Psm, string Ocr, bool Correct);

    static readonly string ResourcesDir = @"E:\wanjizheng\MyProject\iBarter\bin\x86\Debug\net10.0-windows10.0.26100.0\win-x86\Resources";
    static readonly string TessDir = @"E:\wanjizheng\MyProject\iBarter\bin\x86\Debug\net10.0-windows10.0.26100.0\win-x86\tessdata";
    static readonly string TempDir = Path.Combine(Path.GetTempPath(), "ocrbench_" + Guid.NewGuid().ToString("N"));

    static readonly Variant[] Variants = new[]
    {
        new Variant('A', 100, false, 0,  10),
        new Variant('B', 200, false, 0,  10),
        new Variant('C', 300, false, 0,  10),
        new Variant('D', 300, false, 60, 10),
        new Variant('E', 300, false, 70, 10),
        new Variant('F', 300, true,  60, 10),
        new Variant('G', 300, true,  70, 10),
        new Variant('H', 400, false, 60, 10),
        new Variant('I', 400, true,  60, 10),
        new Variant('J', 300, false, 60, 8 ),
        new Variant('K', 300, false, 60, 7 ),
        new Variant('L', 500, false, 60, 10),
        new Variant('M', 300, true,  50, 10),
        new Variant('N', 300, true,  40, 10),
        new Variant('O', 300, false, 80, 10),
        new Variant('P', 600, true,  60, 10),
    };

    static readonly Case[] Cases = new[]
    {
        new Case("ocr_800020.bmp", 1),
        new Case("ocr_800034.bmp", 2),
        new Case("ocr_800055.bmp", 1),
        new Case("ocr_10.bmp",    139),
        new Case("ocr_800018.bmp", 1),
        new Case("ocr_800031.bmp", 3),
        new Case("ocr_9057.bmp",  1000),
        new Case("ocr_800007.bmp", 1),
        new Case("ocr_5855.bmp",  10),
        new Case("ocr_800005.bmp", 1),
        new Case("ocr_6353.bmp",  50),
        new Case("ocr_800004.bmp", 1),
    };

    static void Main()
    {
        Directory.CreateDirectory(TempDir);
        // Copy Tesseract native binaries from bin directory next to our exe
        CopyBinFiles();

        var results = new List<Row>();
        using var tesseract = new Tesseract(TessDir, "eng_best", OcrEngineMode.Default);
        tesseract.SetVariable("tessedit_char_whitelist", "0123456789");
        tesseract.SetVariable("classify_bln_numeric_mode", "1");

        foreach (var c in Cases)
        {
            var srcPath = Path.Combine(ResourcesDir, c.File);
            if (!File.Exists(srcPath))
            {
                Console.Error.WriteLine($"Missing file: {srcPath}");
                continue;
            }
            foreach (var v in Variants)
            {
                string outPath = Path.Combine(TempDir, $"{Path.GetFileNameWithoutExtension(c.File)}_{v.Id}.png");
                try { PrepareImage(srcPath, outPath, v); }
                catch (Exception ex)
                {
                    string inner = ex.InnerException != null ? $" -> {ex.InnerException.GetType().Name}: {ex.InnerException.Message}" : "";
                    Console.Error.WriteLine($"[prep fail] {c.File}/{v.Id}: {ex.Message}{inner}");
                    results.Add(new Row(c.File, c.Expected, v.Id.ToString(), v.Scale, v.Negate, v.Threshold, v.Psm, $"ERR:{ex.Message}{inner}", false));
                    continue;
                }
                string text;
                try
                {
                    using var img = CvInvoke.Imread(outPath, ImreadModes.Grayscale);
                    tesseract.PageSegMode = (PageSegMode)v.Psm;
                    tesseract.SetImage(img);
                    tesseract.Recognize();
                    text = tesseract.GetUTF8Text() ?? "";
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[ocr fail] {c.File}/{v.Id}: {ex.Message}");
                    results.Add(new Row(c.File, c.Expected, v.Id.ToString(), v.Scale, v.Negate, v.Threshold, v.Psm, $"ERR:{ex.Message}", false));
                    continue;
                }
                text = text.Trim();
                bool ok = int.TryParse(text, out var got) && got == c.Expected;
                results.Add(new Row(c.File, c.Expected, v.Id.ToString(), v.Scale, v.Negate, v.Threshold, v.Psm, text, ok));
                Console.WriteLine($"{c.File} v={v.Id,-2} exp={c.Expected,4} got='{text}' {(ok?"OK":"NO")}");
            }
        }

        // Aggregate stats
        var perVariant = results.GroupBy(r => r.Variant)
            .Select(g => new {
                Variant = g.Key,
                Count = g.Count(r => r.Correct),
                Total = g.Count(),
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Variant)
            .ToList();

        string winner = perVariant.First().Variant;
        string winnerCount = $"{perVariant.First().Count}/{perVariant.First().Total}";

        var sb = new StringBuilder();
        sb.AppendLine("Variant wins (most files correct first):");
        foreach (var pv in perVariant)
            sb.AppendLine($"  {pv.Variant}: {pv.Count}/{pv.Total}");

        // Per-digit pattern buckets
        var buckets = new (string Name, Func<int, bool> Match)[] {
            ("single-digit (0-9)", n => n >= 0 && n <= 9),
            ("two-digit (10-99)",   n => n >= 10 && n <= 99),
            ("three-digit (100-999)", n => n >= 100 && n <= 999),
            ("four-digit (1000-9999)", n => n >= 1000 && n <= 9999),
        };
        sb.AppendLine();
        sb.AppendLine("Per-digit-pattern best variant:");
        foreach (var b in buckets)
        {
            var bucketCases = Cases.Where(c => b.Match(c.Expected)).ToList();
            var bucketResults = results.Where(r => bucketCases.Any(c => c.File == r.File)).ToList();
            if (bucketResults.Count == 0) continue;
            var bpv = bucketResults.GroupBy(r => r.Variant)
                .Select(g => new { Variant = g.Key, Count = g.Count(r => r.Correct), Total = g.Count() })
                .OrderByDescending(x => x.Count).ThenBy(x => x.Variant)
                .Take(3)
                .ToList();
            sb.AppendLine($"  {b.Name} (cases={bucketCases.Count(c=>b.Match(c.Expected))}):");
            foreach (var pv in bpv)
                sb.AppendLine($"    {pv.Variant}: {pv.Count}/{pv.Total}");
        }

        var notes = sb.ToString();
        Console.Error.WriteLine();
        Console.Error.WriteLine(notes);

        var json = new
        {
            table = results.Select(r => new {
                file = r.File,
                expected = r.Expected,
                variant = r.Variant,
                scale = r.Scale,
                negate = r.Negate,
                threshold = r.Threshold,
                psm = r.Psm,
                result = r.Ocr,
                parsed = int.TryParse(r.Ocr, out var n) ? (int?)n : null,
                correct = r.Correct,
            }).ToList(),
            notes,
        };
        File.WriteAllText(@"E:\wanjizheng\MyProject\iBarter\_ocrbench\results.json", JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true }));
    }

    static void PrepareImage(string src, string dst, Variant v)
    {
        using var img = new MagickImage(src);
        if (v.Scale != 100)
        {
            int w = (int)Math.Round(img.Width * (v.Scale / 100.0));
            int h = (int)Math.Round(img.Height * (v.Scale / 100.0));
            img.FilterType = FilterType.Lanczos;
            img.Resize(new MagickGeometry(w, h) { IgnoreAspectRatio = true });
        }
        // Always work in grayscale
        img.ColorSpace = ColorSpace.Gray;
        if (v.Negate) img.Negate();
        if (v.Threshold > 0)
        {
            // Magick.NET: Threshold performs strict binary threshold by default
            img.Threshold(new Percentage(v.Threshold));
            img.ColorType = ColorType.Bilevel;
        }
        img.Write(dst, MagickFormat.Png);
    }

    static void CopyBinFiles()
    {
        // Emgu.CV OCR + Magick need their native DLLs to be loaded
        string src = @"E:\wanjizheng\MyProject\iBarter\bin\x86\Debug\net10.0-windows10.0.26100.0\win-x86";
        string dst = AppContext.BaseDirectory;
        foreach (var name in new[] { "tessdata", "libs", "libusb-1.0.dll", "cvextern.dll", "Magick.Native-Q16-x86.dll" })
        {
            var s = Path.Combine(src, name);
            var d = Path.Combine(dst, name);
            if (File.Exists(s)) File.Copy(s, d, true);
            else if (Directory.Exists(s)) CopyDir(s, d);
        }
    }

    static void CopyDir(string s, string d)
    {
        Directory.CreateDirectory(d);
        foreach (var f in Directory.GetFiles(s)) File.Copy(f, Path.Combine(d, Path.GetFileName(f)), true);
        foreach (var sd in Directory.GetDirectories(s))
        {
            var leaf = Path.GetFileName(sd);
            CopyDir(sd, Path.Combine(d, leaf));
        }
    }
}
