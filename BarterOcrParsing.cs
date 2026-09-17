using System;
using System.Text;
using System.Text.RegularExpressions;

namespace iBarter {
    public static class BarterOcrParsing {
        public static bool TryParseRemainingCount(string input, out int remaining) {
            remaining = 0;
            if (string.IsNullOrWhiteSpace(input)) {
                return false;
            }

            string text = ToAsciiDigits(input).Trim();
            if (Regex.IsMatch(text, @"^\s*\d{1,2}\s*[次回]?\s*$") &&
                int.TryParse(Regex.Match(text, @"\d{1,2}").Value, out int bare) &&
                bare >= 0 && bare <= 99) {
                remaining = bare;
                return true;
            }

            string normalized = ChineseTextNormalizer.NormalizeForMatching(text);
            bool hasRemainingMarker =
                normalized.Contains("剩余") ||
                normalized.Contains("剩餘") ||
                normalized.Contains("交換") ||
                normalized.Contains("交换") ||
                normalized.Contains("次數") ||
                normalized.Contains("次数") ||
                text.IndexOf("remaining", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("exchange", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!hasRemainingMarker) {
                return false;
            }

            Match beforeTimes = Regex.Match(text, @"(\d{1,2})\s*[次回]");
            if (beforeTimes.Success &&
                int.TryParse(beforeTimes.Groups[1].Value, out int beforeTimesValue) &&
                beforeTimesValue >= 0 && beforeTimesValue <= 99) {
                remaining = beforeTimesValue;
                return true;
            }

            Match anyDigits = Regex.Match(text, @"\d{1,2}");
            if (anyDigits.Success &&
                int.TryParse(anyDigits.Value, out int value) &&
                value >= 0 && value <= 99) {
                remaining = value;
                return true;
            }

            return false;
        }

        private static string ToAsciiDigits(string input) {
            var sb = new StringBuilder(input.Length);
            foreach (char ch in input) {
                if (ch >= '０' && ch <= '９') {
                    sb.Append((char)('0' + ch - '０'));
                }
                else {
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }
    }
}
