using System;
using System.Text;
using System.Text.RegularExpressions;

namespace FeynmanPet
{
    internal sealed class TtsPreparedText
    {
        public string Text;
        public string Language;
        public bool WasEmpty;
        public bool RemovedCode;
    }

    internal static class TtsTextPreprocessor
    {
        private static readonly Regex FencedCode = new Regex(
            "```[\\s\\S]*?```",
            RegexOptions.Compiled);
        private static readonly Regex MarkdownLink = new Regex(
            "!?\\[([^\\]]+)\\]\\([^\\)]*\\)",
            RegexOptions.Compiled);
        private static readonly Regex HtmlTag = new Regex(
            "<[^>]{1,120}>",
            RegexOptions.Compiled);
        private static readonly Regex Url = new Regex(
            "\\b(?:https?://|www\\.)\\S+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex WindowsPath = new Regex(
            "\\b[A-Za-z]:\\\\[^\\s，。]+",
            RegexOptions.Compiled);
        private static readonly Regex Heading = new Regex(
            "(?m)^\\s{0,3}#{1,6}\\s*",
            RegexOptions.Compiled);
        private static readonly Regex ListMarker = new Regex(
            "(?m)^\\s*(?:[-*+]|\\d+[.)])\\s+",
            RegexOptions.Compiled);
        private static readonly Regex QuoteMarker = new Regex(
            "(?m)^\\s*>\\s*",
            RegexOptions.Compiled);
        private static readonly Regex HorizontalRule = new Regex(
            "(?m)^\\s*[-=_]{3,}\\s*$",
            RegexOptions.Compiled);
        private static readonly Regex RepeatedPunctuation = new Regex(
            "([,，。.!！?？;；:：、])\\1{2,}",
            RegexOptions.Compiled);
        private static readonly Regex Decoration = new Regex(
            "[\\*_~`#>|\\\\]",
            RegexOptions.Compiled);
        private static readonly Regex Emoji = new Regex(
            "[\\uD800-\\uDBFF][\\uDC00-\\uDFFF]|[\\u2600-\\u27BF]",
            RegexOptions.Compiled);
        private static readonly Regex Fractions = new Regex(
            "\\\\frac\\s*\\{([^{}]+)\\}\\s*\\{([^{}]+)\\}",
            RegexOptions.Compiled);
        private static readonly Regex SquareRoots = new Regex(
            "\\\\sqrt\\s*\\{([^{}]+)\\}",
            RegexOptions.Compiled);
        private static readonly Regex PercentWithNumber = new Regex(
            "(\\d+(?:\\.\\d+)?)\\s*%",
            RegexOptions.Compiled);
        private static readonly Regex Whitespace = new Regex(
            "\\s+",
            RegexOptions.Compiled);
        private static readonly Regex BrokenPunctuation = new Regex(
            "\\s+([,，。.!！?？;；:：、])",
            RegexOptions.Compiled);
        private static readonly Regex RepeatedCommas = new Regex(
            ",\\s*,|，\\s*，",
            RegexOptions.Compiled);

        public static TtsPreparedText Prepare(string original, string languagePreference)
        {
            TtsPreparedText result = new TtsPreparedText();
            result.Language = LanguageTools.Resolve(languagePreference, original);
            bool chinese = result.Language == "zh";
            string text = original ?? "";
            bool hasCode = FencedCode.IsMatch(text) || text.IndexOf('`') >= 0;
            result.RemovedCode = hasCode;
            if (hasCode)
            {
                text = FencedCode.Replace(
                    text,
                    chinese ? " 代码片段已跳过。 " : " Code sample omitted. ");
            }

            text = MarkdownLink.Replace(text, "$1");
            text = HtmlTag.Replace(text, " ");
            text = Url.Replace(text, chinese ? " 链接 " : " link ");
            text = WindowsPath.Replace(text, chinese ? " 文件路径 " : " file path ");
            text = Heading.Replace(text, "");
            text = ListMarker.Replace(text, "");
            text = QuoteMarker.Replace(text, "");
            text = HorizontalRule.Replace(text, " ");
            text = text.Replace("`", " ");
            text = text.Replace("**", "").Replace("__", "");
            text = text.Replace("~~", "");
            text = text.Replace("*", "").Replace("_", " ");
            text = text.Replace("|", chinese ? "，" : ", ");
            text = text.Replace("[", " ").Replace("]", " ");
            text = text.Replace("{", " ").Replace("}", " ");
            text = text.Replace("<", " ").Replace(">", " ");
            text = text.Replace("“", "").Replace("”", "");
            text = text.Replace("‘", "").Replace("’", "");
            text = text.Replace("(", chinese ? "，" : ", ");
            text = text.Replace(")", " ");
            text = text.Replace("（", "，").Replace("）", " ");
            text = Fractions.Replace(
                text,
                chinese ? "$1 除以 $2" : "$1 divided by $2");
            text = SquareRoots.Replace(
                text,
                chinese ? "根号 $1" : "square root of $1");
            text = text.Replace("\\times", chinese ? " 乘以 " : " times ");
            text = text.Replace("\\cdot", chinese ? " 乘以 " : " times ");
            text = text.Replace("\\approx", chinese ? " 约等于 " : " approximately ");
            text = text.Replace("\\le", chinese ? " 小于等于 " : " less than or equal to ");
            text = text.Replace("\\ge", chinese ? " 大于等于 " : " greater than or equal to ");
            text = text.Replace("\\", " ");
            text = PercentWithNumber.Replace(
                text,
                chinese ? "百分之$1" : "$1 percent");
            text = text.Replace("°C", chinese ? "摄氏度" : " degrees Celsius");
            text = text.Replace("×", chinese ? "乘以" : " times ");
            text = text.Replace("÷", chinese ? "除以" : " divided by ");
            text = text.Replace("≈", chinese ? "约等于" : " approximately ");
            text = text.Replace("±", chinese ? "正负" : " plus or minus ");
            text = text.Replace("=", chinese ? "等于" : " equals ");
            text = text.Replace(" + ", chinese ? " 加 " : " plus ");
            text = text.Replace(" - ", chinese ? " 减 " : " minus ");
            text = text.Replace("/", chinese ? "除以" : " divided by ");
            text = text.Replace("&", chinese ? "和" : " and ");
            text = text.Replace("C++", "C plus plus");
            text = text.Replace("C#", "C sharp");
            if (result.Language == "en")
            {
                text = Regex.Replace(
                    text,
                    "\\be\\.g\\.",
                    "for example",
                    RegexOptions.IgnoreCase);
                text = Regex.Replace(
                    text,
                    "\\bi\\.e\\.",
                    "that is",
                    RegexOptions.IgnoreCase);
                text = Regex.Replace(
                    text,
                    "\\bvs\\.",
                    "versus",
                    RegexOptions.IgnoreCase);
            }
            text = Emoji.Replace(text, " ");
            text = Decoration.Replace(text, " ");
            text = text.Replace("^2", chinese ? "的平方" : " squared");
            text = text.Replace("^3", chinese ? "的立方" : " cubed");
            text = text.Replace("^", " ");
            text = RepeatedCommas.Replace(text, chinese ? "，" : ", ");
            text = RepeatedPunctuation.Replace(text, "$1");
            text = BrokenPunctuation.Replace(text, "$1");
            text = Whitespace.Replace(text, " ").Trim();
            text = text.Trim(',', '，', '、', ';', '；');
            text = Whitespace.Replace(text, " ").Trim();
            if (!ContainsSpeakableText(text))
            {
                text = "";
            }
            result.Text = text;
            result.WasEmpty = text.Length == 0;
            return result;
        }

        private static bool ContainsSpeakableText(string text)
        {
            foreach (char character in text)
            {
                if (Char.IsLetterOrDigit(character) ||
                    (character >= 0x3400 && character <= 0x9FFF))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
