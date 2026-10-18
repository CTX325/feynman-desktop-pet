using System;

namespace FeynmanPet
{
    internal static class LanguageTools
    {
        public static string Resolve(string configuredLanguage, string text)
        {
            string normalized = AssistantSettings.NormalizeLanguage(configuredLanguage);
            if (normalized != "auto")
            {
                return normalized;
            }
            return DetectPrimaryLanguage(text);
        }

        public static string DetectPrimaryLanguage(string text)
        {
            if (String.IsNullOrWhiteSpace(text))
            {
                return "en";
            }
            int chinese = 0;
            int latin = 0;
            foreach (char character in text)
            {
                if ((character >= 0x3400 && character <= 0x9FFF) ||
                    (character >= 0xF900 && character <= 0xFAFF))
                {
                    chinese++;
                }
                else if ((character >= 'A' && character <= 'Z') ||
                    (character >= 'a' && character <= 'z'))
                {
                    latin++;
                }
            }
            if (chinese == 0)
            {
                return "en";
            }
            if (latin == 0)
            {
                return "zh";
            }
            // A Han character generally carries more semantic weight than one Latin letter.
            return chinese * 2 >= latin ? "zh" : "en";
        }
    }
}
