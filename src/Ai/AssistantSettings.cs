using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace FeynmanPet
{
    internal sealed class AssistantSettings
    {
        public const string DefaultPrompt =
            "You are a friendly science tutor inspired by Richard Feynman's teaching style. " +
            "Explain difficult ideas clearly, intuitively, and with curiosity, humor, and " +
            "concrete examples. Be intellectually honest and distinguish established facts " +
            "from uncertainty. You are an AI assistant inspired by a teaching style, not " +
            "Richard Feynman himself.";

        public string RecognitionLanguage = "auto";
        public string OutputLanguage = "auto";
        public string TtsService = "espeak";
        public string ChineseVoiceName = "";
        public string EnglishVoiceName = "";
        public string CloudVoiceId = "";
        public string CloudModelId = "eleven_multilingual_v2";
        public string DeepSeekModel = "deepseek-chat";
        public int TtsRate = 0;
        public bool AutoSpeak = true;
        public bool ShowMicButton = true;
        public string SystemPrompt = DefaultPrompt;

        public static string GetPath(string projectRoot)
        {
            return Path.Combine(projectRoot, "data", "assistant-settings.ini");
        }

        public static AssistantSettings Load(string projectRoot)
        {
            AssistantSettings settings = new AssistantSettings();
            string path = GetPath(projectRoot);
            if (!File.Exists(path))
            {
                return settings;
            }
            try
            {
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    int split = line.IndexOf('=');
                    if (split <= 0)
                    {
                        continue;
                    }
                    string key = line.Substring(0, split).Trim().ToLowerInvariant();
                    string value = line.Substring(split + 1).Trim();
                    if (key == "recognition_language")
                    {
                        settings.RecognitionLanguage = NormalizeLanguage(value);
                    }
                    else if (key == "output_language" || key == "answer_language")
                    {
                        settings.OutputLanguage = NormalizeLanguage(value);
                    }
                    else if (key == "tts_service")
                    {
                        settings.TtsService =
                            value == "espeak" || value == "cloud" ? value : "windows";
                    }
                    else if (key == "local_voice_name")
                    {
                        settings.ChineseVoiceName = value;
                        settings.EnglishVoiceName = value;
                    }
                    else if (key == "chinese_voice_name")
                    {
                        settings.ChineseVoiceName = value;
                    }
                    else if (key == "english_voice_name")
                    {
                        settings.EnglishVoiceName = value;
                    }
                    else if (key == "cloud_voice_id")
                    {
                        settings.CloudVoiceId = value;
                    }
                    else if (key == "cloud_model_id" && value.Length > 0)
                    {
                        settings.CloudModelId = value;
                    }
                    else if (key == "deepseek_model" && value.Length > 0)
                    {
                        settings.DeepSeekModel = value;
                    }
                    else if (key == "tts_rate")
                    {
                        int rate;
                        if (Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out rate))
                        {
                            settings.TtsRate = Math.Max(-6, Math.Min(6, rate));
                        }
                    }
                    else if (key == "auto_speak")
                    {
                        settings.AutoSpeak = value == "1" ||
                            String.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (key == "show_mic_button")
                    {
                        settings.ShowMicButton = value == "1" ||
                            String.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (key == "system_prompt_base64" && value.Length > 0)
                    {
                        try
                        {
                            string prompt = Encoding.UTF8.GetString(Convert.FromBase64String(value));
                            if (prompt.Trim().Length > 0)
                            {
                                settings.SystemPrompt = prompt;
                            }
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                RuntimeLog.Write("Assistant settings read failed: " + exception.Message);
            }
            return settings;
        }

        public void Save(string projectRoot)
        {
            try
            {
                string path = GetPath(projectRoot);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                RecognitionLanguage = NormalizeLanguage(RecognitionLanguage);
                OutputLanguage = NormalizeLanguage(OutputLanguage);
                TtsRate = Math.Max(-6, Math.Min(6, TtsRate));
                StringBuilder text = new StringBuilder();
                text.AppendLine("# Feynman desktop pet local assistant preferences.");
                text.AppendLine("# This file intentionally contains no API keys.");
                text.AppendLine("recognition_language=" + RecognitionLanguage);
                text.AppendLine("output_language=" + OutputLanguage);
                text.AppendLine("answer_language=" + OutputLanguage);
                text.AppendLine("tts_service=" + TtsService);
                text.AppendLine("chinese_voice_name=" + ChineseVoiceName);
                text.AppendLine("english_voice_name=" + EnglishVoiceName);
                text.AppendLine("cloud_voice_id=" + CloudVoiceId);
                text.AppendLine("cloud_model_id=" + CloudModelId);
                text.AppendLine("deepseek_model=" + DeepSeekModel);
                text.AppendLine("tts_rate=" + TtsRate.ToString(CultureInfo.InvariantCulture));
                text.AppendLine("auto_speak=" + (AutoSpeak ? "1" : "0"));
                text.AppendLine("show_mic_button=" + (ShowMicButton ? "1" : "0"));
                text.AppendLine(
                    "system_prompt_base64=" +
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(SystemPrompt ?? DefaultPrompt)));
                File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                RuntimeLog.Write("Assistant settings write failed: " + exception.Message);
            }
        }

        public static string NormalizeLanguage(string language)
        {
            if (String.Equals(language, "zh", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(language, "zh-CN", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(language, "chinese", StringComparison.OrdinalIgnoreCase))
            {
                return "zh";
            }
            if (String.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(language, "english", StringComparison.OrdinalIgnoreCase))
            {
                return "en";
            }
            return "auto";
        }
    }
}
