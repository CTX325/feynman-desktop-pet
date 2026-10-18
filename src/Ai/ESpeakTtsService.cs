using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FeynmanPet
{
    internal sealed class ESpeakTtsService : IVoiceOutputService
    {
        private readonly object sync = new object();
        private readonly string executablePath;
        private List<VoiceDescriptor> voices;
        private Process activeProcess;
        private int busy;
        private volatile bool stopRequested;
        private bool disposed;

        public event EventHandler<VoiceCompletedEventArgs> SpeechCompleted;
        public event EventHandler<VoiceFailureEventArgs> SpeechFailed;

        public ESpeakTtsService()
        {
            executablePath = FindExecutable();
        }

        public string Id
        {
            get { return "espeak"; }
        }

        public string DisplayName
        {
            get { return "eSpeak NG 本地男声"; }
        }

        public bool IsAvailable
        {
            get { return GetVoices().Count > 0; }
        }

        public string AvailabilityMessage
        {
            get
            {
                if (String.IsNullOrEmpty(executablePath))
                {
                    return "未找到 eSpeak NG。安装 CLI 版本后无需注册 SAPI，本程序会直接调用引擎。";
                }
                IList<VoiceDescriptor> available = GetVoices();
                if (available.Count == 0)
                {
                    return "已找到 eSpeak NG，但没有解析到可用的中文或英文男声。";
                }
                StringBuilder text = new StringBuilder();
                text.Append("eSpeak NG: ");
                for (int index = 0; index < available.Count; index++)
                {
                    if (index > 0)
                    {
                        text.Append("; ");
                    }
                    text.Append(available[index].Name);
                    text.Append(" (");
                    text.Append(available[index].CultureName);
                    text.Append(", ");
                    text.Append(available[index].Gender);
                    text.Append(")");
                }
                return text.ToString();
            }
        }

        public IList<VoiceDescriptor> GetVoices()
        {
            lock (sync)
            {
                if (voices != null)
                {
                    return new List<VoiceDescriptor>(voices);
                }
            }
            List<VoiceDescriptor> loaded = LoadVoices();
            lock (sync)
            {
                if (voices == null)
                {
                    voices = loaded;
                }
                return new List<VoiceDescriptor>(voices);
            }
        }

        public bool SupportsLanguage(string language)
        {
            string wanted = AssistantSettings.NormalizeLanguage(language);
            foreach (VoiceDescriptor voice in GetVoices())
            {
                if (wanted == "auto" ||
                    (wanted == "zh" && voice.CultureName.StartsWith(
                        "zh", StringComparison.OrdinalIgnoreCase)) ||
                    (wanted == "en" && voice.CultureName.StartsWith(
                        "en", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            return false;
        }

        public async Task SpeakAsync(
            string text,
            string language,
            int rate,
            string voiceName,
            CancellationToken cancellationToken)
        {
            if (disposed)
            {
                throw new ObjectDisposedException("ESpeakTtsService");
            }
            if (String.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("朗读文本为空。", "text");
            }
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0)
            {
                throw new InvalidOperationException("上一段 eSpeak 语音仍在播放，请先停止。");
            }

            stopRequested = false;
            string resolved = ResolveLanguage(text, language);
            VoiceDescriptor voice = FindVoice(resolved, voiceName);
            if (voice == null)
            {
                Interlocked.Exchange(ref busy, 0);
                throw new InvalidOperationException(
                    resolved == "zh"
                        ? "eSpeak NG 没有可用的中文男声。"
                        : "eSpeak NG 没有可用的英文男声。");
            }

            CancellationTokenRegistration registration = cancellationToken.Register(
                delegate { StopSpeaking(); });
            try
            {
                int speed = Math.Max(80, Math.Min(300, 160 + rate * 15));
                int pitch = resolved == "zh" ? 35 : 38;
                string arguments = String.Format(
                    "-v {0} -s {1} -p {2} -a 100 -m",
                    voice.VoiceId,
                    speed,
                    pitch);
                int exitCode = await Task.Run(
                    new Func<int>(delegate
                    {
                        return RunESpeak(arguments, text);
                    }));
                if (stopRequested || cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                if (exitCode != 0)
                {
                    throw new InvalidOperationException(
                        "eSpeak NG 播放失败，退出码 " + exitCode + "。");
                }
                RuntimeLog.Write(
                    "eSpeak TTS completed. voice=" + voice.VoiceId +
                    " culture=" + voice.CultureName);
                EventHandler<VoiceCompletedEventArgs> completed = SpeechCompleted;
                if (completed != null)
                {
                    completed(this, new VoiceCompletedEventArgs(false));
                }
            }
            catch (Exception exception)
            {
                bool cancelled =
                    stopRequested ||
                    cancellationToken.IsCancellationRequested ||
                    exception is OperationCanceledException;
                if (cancelled)
                {
                    OperationCanceledException canceled =
                        new OperationCanceledException(cancellationToken);
                    EventHandler<VoiceCompletedEventArgs> stopped = SpeechCompleted;
                    if (stopped != null)
                    {
                        stopped(this, new VoiceCompletedEventArgs(true));
                    }
                    throw canceled;
                }
                RuntimeLog.Write("eSpeak TTS failed: " + exception);
                EventHandler<VoiceFailureEventArgs> failed = SpeechFailed;
                if (failed != null)
                {
                    failed(this, new VoiceFailureEventArgs(exception.Message));
                }
                throw;
            }
            finally
            {
                lock (sync)
                {
                    activeProcess = null;
                }
                registration.Dispose();
                Interlocked.Exchange(ref busy, 0);
            }
        }

        public void StopSpeaking()
        {
            stopRequested = true;
            Process process;
            lock (sync)
            {
                process = activeProcess;
            }
            if (process != null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch
                {
                }
            }
        }

        private int RunESpeak(string arguments, string text)
        {
            Process process = new Process();
            process.StartInfo.FileName = executablePath;
            process.StartInfo.Arguments = arguments;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.RedirectStandardInput = true;
            process.StartInfo.RedirectStandardError = true;
            try
            {
                process.Start();
                lock (sync)
                {
                    activeProcess = process;
                }
                if (stopRequested)
                {
                    StopSpeaking();
                    return 1;
                }
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                process.StandardInput.BaseStream.Write(bytes, 0, bytes.Length);
                process.StandardInput.Close();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                string error = errorTask.Result;
                if (process.ExitCode != 0 && !String.IsNullOrEmpty(error))
                {
                    RuntimeLog.Write("eSpeak stderr: " + ShortError(error));
                }
                return process.ExitCode;
            }
            finally
            {
                try
                {
                    process.Dispose();
                }
                catch
                {
                }
            }
        }

        private List<VoiceDescriptor> LoadVoices()
        {
            List<VoiceDescriptor> result = new List<VoiceDescriptor>();
            if (String.IsNullOrEmpty(executablePath))
            {
                return result;
            }
            try
            {
                ProcessStartInfo info = new ProcessStartInfo(executablePath, "--voices");
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                using (Process process = Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();
                    VoiceDescriptor chinese = null;
                    VoiceDescriptor englishUs = null;
                    VoiceDescriptor englishGb = null;
                    VoiceDescriptor firstEnglish = null;
                    foreach (string line in output.Split(
                        new string[] { "\r\n", "\n" },
                        StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] parts = line.Split(
                            new char[] { ' ', '\t' },
                            StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 4 ||
                            String.Equals(parts[0], "Pty", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        string languageCode = parts[1];
                        string genderCode = parts[2];
                        if (genderCode.IndexOf("/M", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }
                        VoiceDescriptor voice = new VoiceDescriptor();
                        voice.Provider = "espeak";
                        voice.VoiceId = languageCode;
                        voice.Gender = "Male";
                        voice.Age = "Unknown";
                        string display = parts[3].Replace('_', ' ');
                        if (languageCode == "cmn" || languageCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                        {
                            voice.CultureName = "zh-CN";
                            voice.Name = "eSpeak NG · " + display + " (Male)";
                            if (chinese == null || languageCode == "cmn")
                            {
                                chinese = voice;
                            }
                        }
                        else if (languageCode == "en-us" ||
                            languageCode.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                        {
                            voice.CultureName = languageCode == "en-gb" ? "en-GB" : "en-US";
                            voice.Name = "eSpeak NG · " + display + " (Male)";
                            if (languageCode == "en-us" && englishUs == null)
                            {
                                englishUs = voice;
                            }
                            else if (languageCode == "en-gb" && englishGb == null)
                            {
                                englishGb = voice;
                            }
                            else if (firstEnglish == null)
                            {
                                firstEnglish = voice;
                            }
                        }
                    }
                    if (chinese != null)
                    {
                        result.Add(chinese);
                    }
                    if (englishUs != null)
                    {
                        result.Add(englishUs);
                    }
                    else if (englishGb != null)
                    {
                        result.Add(englishGb);
                    }
                    else if (firstEnglish != null)
                    {
                        result.Add(firstEnglish);
                    }
                }
                if (result.Count > 0)
                {
                    RuntimeLog.Write("eSpeak voices loaded from " + executablePath);
                }
            }
            catch (Exception exception)
            {
                RuntimeLog.Write("eSpeak voice enumeration failed: " + exception.Message);
            }
            return result;
        }

        private VoiceDescriptor FindVoice(string language, string preferredName)
        {
            IList<VoiceDescriptor> available = GetVoices();
            if (!String.IsNullOrWhiteSpace(preferredName))
            {
                foreach (VoiceDescriptor voice in available)
                {
                    if (String.Equals(
                        voice.Name,
                        preferredName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return voice;
                    }
                }
            }
            foreach (VoiceDescriptor voice in available)
            {
                if (voice.CultureName.StartsWith(language, StringComparison.OrdinalIgnoreCase))
                {
                    return voice;
                }
            }
            return null;
        }

        private static string ResolveLanguage(string text, string configured)
        {
            string language = AssistantSettings.NormalizeLanguage(configured);
            if (language != "auto")
            {
                return language;
            }
            foreach (char character in text)
            {
                if (character >= 0x3400 && character <= 0x9FFF)
                {
                    return "zh";
                }
            }
            return "en";
        }

        private static string FindExecutable()
        {
            string[] candidates = new string[]
            {
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "eSpeak NG",
                    "espeak-ng.exe"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "eSpeak NG",
                    "espeak-ng.exe")
            };
            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static string ShortError(string text)
        {
            string value = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return value.Length > 180 ? value.Substring(0, 180) : value;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            StopSpeaking();
        }
    }
}
