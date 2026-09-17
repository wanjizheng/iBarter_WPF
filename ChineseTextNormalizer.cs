using System;
using System.Runtime.InteropServices;
using System.Text;

namespace iBarter {
    internal static class ChineseTextNormalizer {
        private const uint LCMAP_TRADITIONAL_CHINESE = 0x04000000;
        private const uint LCMAP_SIMPLIFIED_CHINESE = 0x02000000;

        public static string ToSimplifiedChinese(string input) =>
            ConvertChinese(input, LCMAP_SIMPLIFIED_CHINESE, MapTraditionalVariant);

        public static string NormalizeForMatching(string input) {
            if (string.IsNullOrWhiteSpace(input)) {
                return string.Empty;
            }

            // Fast path: ASCII / pinyin inputs dominate IME composition
            // and English label edits. Skip the LCMapStringEx P/Invoke
            // and the per-char CJK compaction - only the OCR-variant
            // map can change anything. This drops the per-keystroke
            // cost to one StringBuilder pass for the most common case.
            if (!ContainsCjk(input)) {
                var ascii = new StringBuilder(input.Length);
                foreach (char ch in input) {
                    ascii.Append(MapOcrVariant(ch));
                }
                return ascii.ToString();
            }

            string text = ToTraditionalChinese(input);
            bool hasCjk = ContainsCjk(text);

            var mapped = new StringBuilder(text.Length);
            foreach (char ch in text) {
                mapped.Append(MapOcrVariant(ch));
            }

            if (!hasCjk) {
                return mapped.ToString();
            }

            // Build compact directly from mapped via index access so we
            // don't materialize `mapped.ToString()` once per iteration
            // (the previous version did, allocating a fresh string on
            // every char of the loop).
            var compact = new StringBuilder(mapped.Length);
            for (int i = 0; i < mapped.Length; i++) {
                char ch = mapped[i];
                if (ContainsCjk(ch) || char.IsLetterOrDigit(ch)) {
                    compact.Append(ch);
                }
            }
            return compact.ToString();
        }

        public static bool ContainsCjk(string input) {
            if (string.IsNullOrEmpty(input)) {
                return false;
            }

            foreach (char ch in input) {
                if (ContainsCjk(ch)) {
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsCjk(char ch) {
            return ch >= 0x4E00 && ch <= 0x9FFF;
        }

        private static char MapOcrVariant(char ch) {
            return ch switch {
                '僻' => '島',
                '伺' => '島',
                '鸟' => '島',
                _ => ch,
            };
        }

        private static string ToTraditionalChinese(string input) {
            if (string.IsNullOrEmpty(input) || !ContainsCjk(input)) {
                return input ?? string.Empty;
            }

            try {
                var buffer = new StringBuilder(input.Length * 2);
                int length = LCMapStringEx(
                    "zh-CN",
                    LCMAP_TRADITIONAL_CHINESE,
                    input,
                    -1,
                    buffer,
                    buffer.Capacity,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero);

                if (length > 0) {
                    string converted = buffer.ToString();
                    int terminator = converted.IndexOf('\0');
                    if (terminator >= 0) {
                        return converted.Substring(0, terminator);
                    }
                    int count = Math.Min(length - 1, converted.Length);
                    return count > 0 ? converted.Substring(0, count) : string.Empty;
                }
            }
            catch {
                // Fall through to the small manual map below.  The app is
                // Windows-only, but keeping a fallback makes unit-style checks
                // and unusual runtimes less brittle.
            }

            var mapped = new StringBuilder(input.Length);
            foreach (char ch in input) {
                mapped.Append(MapSimplifiedVariant(ch));
            }
            return mapped.ToString();
        }

        private static string ConvertChinese(
            string input,
            uint conversion,
            Func<char, char> fallback) {
            if (string.IsNullOrEmpty(input) || !ContainsCjk(input)) return input ?? string.Empty;
            try {
                var buffer = new StringBuilder(input.Length * 2);
                int length = LCMapStringEx(
                    "zh-CN", conversion, input, -1, buffer, buffer.Capacity,
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (length > 0) {
                    string converted = buffer.ToString();
                    int terminator = converted.IndexOf('\0');
                    if (terminator >= 0) return converted[..terminator];
                    int count = Math.Min(length - 1, converted.Length);
                    return count > 0 ? converted[..count] : string.Empty;
                }
            }
            catch {
                // Windows normally provides LCMapStringEx. Keep a small fallback
                // for tests and unusual runtimes.
            }
            var mapped = new StringBuilder(input.Length);
            foreach (char character in input) mapped.Append(fallback(character));
            return mapped.ToString();
        }

        private static char MapTraditionalVariant(char character) => character switch {
            '體' => '体', '視' => '视', '窗' => '窗', '計' => '计', '畫' => '画',
            '儲' => '储', '讀' => '读', '載' => '载', '組' => '组', '烏' => '乌',
            '鴉' => '鸦', '幣' => '币', '優' => '优', '賺' => '赚', '錢' => '钱',
            '補' => '补', '貨' => '货', '規' => '规', '劃' => '划', '貢' => '贡',
            '獻' => '献', '輸' => '输', '產' => '产', '庫' => '库', '達' => '达',
            '預' => '预', '設' => '设', '種' => '种', '圖' => '图', '擷' => '撷',
            '將' => '将', '選' => '选', '筆' => '笔', '貝' => '贝', '爾' => '尔',
            '亞' => '亚', '島' => '岛', '識' => '识', '資' => '资', '訊' => '讯',
            '鏈' => '链', '徑' => '径', '確' => '确', '刪' => '删', '檔' => '档',
            _ => character,
        };

        private static char MapSimplifiedVariant(char ch) {
            return ch switch {
                '岛' => '島',
                '玛' => '瑪',
                '亚' => '亞',
                '尔' => '爾',
                '卢' => '盧',
                '乌' => '烏',
                '鸦' => '鴉',
                '巢' => '巢',
                '汉' => '漢',
                '龙' => '龍',
                '药' => '藥',
                '酒' => '酒',
                '画' => '畫',
                '灯' => '燈',
                '绳' => '繩',
                '铁' => '鐵',
                '铜' => '銅',
                '银' => '銀',
                '盐' => '鹽',
                '软' => '軟',
                '优' => '優',
                '质' => '質',
                '书' => '書',
                '图' => '圖',
                '纸' => '紙',
                '线' => '線',
                '丝' => '絲',
                '箱' => '箱',
                '贝' => '貝',
                '庄' => '莊',
                '萨' => '薩',
                '扇' => '扇',
                '营' => '營',
                '地' => '地',
                '圣' => '聖',
                '殿' => '殿',
                '侦' => '偵',
                '查' => '查',
                '觉' => '覺',
                '术' => '術',
                '遗' => '遺',
                '迹' => '跡',
                '龟' => '龜',
                '湾' => '灣',
                '麦' => '麥',
                '蓝' => '藍',
                '赛' => '賽',
                '达' => '達',
                '门' => '門',
                '币' => '幣',
                '钟' => '鐘',
                '旧' => '舊',
                '号' => '號',
                '颗' => '顆',
                '丢' => '丟',
                '失' => '失',
                '装' => '裝',
                '饰' => '飾',
                '骑' => '騎',
                '团' => '團',
                '贼' => '賊',
                '维' => '維',
                '滨' => '濱',
                '舰' => '艦',
                '桥' => '橋',
                _ => ch,
            };
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern int LCMapStringEx(
            string lpLocaleName,
            uint dwMapFlags,
            string lpSrcStr,
            int cchSrc,
            StringBuilder lpDestStr,
            int cchDest,
            IntPtr lpVersionInformation,
            IntPtr lpReserved,
            IntPtr sortHandle);
    }
}
