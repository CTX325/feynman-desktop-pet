using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace FeynmanPet
{
    internal sealed class VoiceDescriptor
    {
        public string Provider;
        public string VoiceId;
        public string Name;
        public string CultureName;
        public string Gender;
        public string Age;
    }

    internal sealed class VoiceFailureEventArgs : EventArgs
    {
        public readonly string Message;

        public VoiceFailureEventArgs(string message)
        {
            Message = message;
        }
    }

    internal sealed class VoiceCompletedEventArgs : EventArgs
    {
        public readonly bool Cancelled;

        public VoiceCompletedEventArgs(bool cancelled)
        {
            Cancelled = cancelled;
        }
    }

    internal interface IVoiceOutputService : IDisposable
    {
        string Id { get; }
        string DisplayName { get; }
        bool IsAvailable { get; }
        string AvailabilityMessage { get; }
        IList<VoiceDescriptor> GetVoices();
        bool SupportsLanguage(string language);
        Task SpeakAsync(
            string text,
            string language,
            int rate,
            string voiceName,
            CancellationToken cancellationToken);
        void StopSpeaking();
        event EventHandler<VoiceCompletedEventArgs> SpeechCompleted;
        event EventHandler<VoiceFailureEventArgs> SpeechFailed;
    }

    internal sealed class SapiVoiceToken
    {
        public object Token;
        public string Name;
        public string CultureName;
        public string Gender;
        public string Age;
    }

    internal static class SapiCom
    {
        public static object CreateVoice()
        {
            Type type = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (type == null)
            {
                throw new InvalidOperationException("Windows SAPI voice COM class is unavailable.");
            }
            return Activator.CreateInstance(type);
        }

        public static object GetVoices(object voice)
        {
            return voice.GetType().InvokeMember(
                "GetVoices",
                BindingFlags.InvokeMethod,
                null,
                voice,
                null);
        }

        public static int Count(object collection)
        {
            object value = collection.GetType().InvokeMember(
                "Count",
                BindingFlags.GetProperty,
                null,
                collection,
                null);
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        public static object Item(object collection, int index)
        {
            return collection.GetType().InvokeMember(
                "Item",
                BindingFlags.InvokeMethod,
                null,
                collection,
                new object[] { index });
        }

        public static string Description(object token)
        {
            object value = token.GetType().InvokeMember(
                "GetDescription",
                BindingFlags.InvokeMethod,
                null,
                token,
                new object[] { 0 });
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        }

        public static string Attribute(object token, string name)
        {
            try
            {
                object value = token.GetType().InvokeMember(
                    "GetAttribute",
                    BindingFlags.InvokeMethod,
                    null,
                    token,
                    new object[] { name });
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            }
            catch
            {
                return "";
            }
        }

        public static List<SapiVoiceToken> DescribeVoices()
        {
            List<SapiVoiceToken> result = new List<SapiVoiceToken>();
            object voice = null;
            object collection = null;
            try
            {
                voice = CreateVoice();
                collection = GetVoices(voice);
                int count = Count(collection);
                for (int index = 0; index < count; index++)
                {
                    object token = null;
                    try
                    {
                        token = Item(collection, index);
                        SapiVoiceToken item = new SapiVoiceToken();
                        item.Token = token;
                        item.Name = Description(token);
                        item.CultureName = CultureFromLanguage(Attribute(token, "Language"));
                        item.Gender = Attribute(token, "Gender");
                        item.Age = Attribute(token, "Age");
                        result.Add(item);
                    }
                    catch
                    {
                        Release(token);
                    }
                }
            }
            finally
            {
                Release(collection);
                Release(voice);
            }
            return result;
        }

        public static void SelectVoice(object voice, object token)
        {
            voice.GetType().InvokeMember(
                "Voice",
                BindingFlags.SetProperty,
                null,
                voice,
                new object[] { token });
        }

        public static void SetInteger(object voice, string property, int value)
        {
            voice.GetType().InvokeMember(
                property,
                BindingFlags.SetProperty,
                null,
                voice,
                new object[] { value });
        }

        public static int Speak(object voice, string text, int flags)
        {
            try
            {
                object result = voice.GetType().InvokeMember(
                    "Speak",
                    BindingFlags.InvokeMethod,
                    null,
                    voice,
                    new object[] { text, flags });
                return Convert.ToInt32(result, CultureInfo.InvariantCulture);
            }
            catch (TargetInvocationException exception)
            {
                if (exception.InnerException != null)
                {
                    throw new InvalidOperationException(
                        exception.InnerException.Message,
                        exception.InnerException);
                }
                throw;
            }
        }

        public static bool WaitUntilDone(object voice, int milliseconds)
        {
            try
            {
                object result = voice.GetType().InvokeMember(
                    "WaitUntilDone",
                    BindingFlags.InvokeMethod,
                    null,
                    voice,
                    new object[] { milliseconds });
                return Convert.ToBoolean(result, CultureInfo.InvariantCulture);
            }
            catch (TargetInvocationException exception)
            {
                if (exception.InnerException != null)
                {
                    throw new InvalidOperationException(
                        exception.InnerException.Message,
                        exception.InnerException);
                }
                throw;
            }
        }

        public static void Release(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                try
                {
                    Marshal.ReleaseComObject(value);
                }
                catch
                {
                }
            }
        }

        private static string CultureFromLanguage(string language)
        {
            if (String.IsNullOrWhiteSpace(language))
            {
                return "unknown";
            }
            string first = language.Split(';')[0].Trim();
            int lcid;
            if (Int32.TryParse(
                first,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out lcid))
            {
                try
                {
                    return CultureInfo.GetCultureInfo(lcid).Name;
                }
                catch
                {
                }
            }
            return "unknown";
        }
    }

    internal sealed class WindowsTtsService : IVoiceOutputService
    {
        private readonly object sync = new object();
        private readonly DispatcherTimer completionTimer;
        private object currentVoice;
        private TaskCompletionSource<object> currentTask;
        private CancellationTokenRegistration cancellationRegistration;
        private int speechStartedTick;
        private bool stopRequested;
        private bool disposed;

        public event EventHandler<VoiceCompletedEventArgs> SpeechCompleted;
        public event EventHandler<VoiceFailureEventArgs> SpeechFailed;

        public WindowsTtsService()
        {
            completionTimer = new DispatcherTimer(DispatcherPriority.Background);
            completionTimer.Interval = TimeSpan.FromMilliseconds(120);
            completionTimer.Tick += CompletionTimerTick;
        }

        public string Id
        {
            get { return "windows"; }
        }

        public string DisplayName
        {
            get { return "Windows 本地语音"; }
        }

        public bool IsAvailable
        {
            get { return GetVoices().Count > 0; }
        }

        public string AvailabilityMessage
        {
            get
            {
                IList<VoiceDescriptor> voices = GetVoices();
                if (voices.Count == 0)
                {
                    return "Windows 没有可用的 SAPI 语音。请安装中英文 Windows 语音。";
                }
                StringBuilder text = new StringBuilder();
                text.Append("可用声音: ");
                for (int index = 0; index < voices.Count; index++)
                {
                    if (index > 0)
                    {
                        text.Append("; ");
                    }
                    text.Append(voices[index].Name);
                    text.Append(" (");
                    text.Append(voices[index].CultureName);
                    text.Append(", ");
                    text.Append(voices[index].Gender);
                    text.Append(")");
                }
                text.Append("。播放还需要可用的默认音频输出设备。");
                return text.ToString();
            }
        }

        public IList<VoiceDescriptor> GetVoices()
        {
            List<VoiceDescriptor> result = new List<VoiceDescriptor>();
            List<SapiVoiceToken> native;
            try
            {
                native = SapiCom.DescribeVoices();
            }
            catch (Exception exception)
            {
                RuntimeLog.Write("SAPI voice enumeration failed: " + exception);
                return result;
            }
            try
            {
                foreach (SapiVoiceToken token in native)
                {
                    VoiceDescriptor voice = new VoiceDescriptor();
                    voice.Provider = "windows";
                    voice.VoiceId = token.Name;
                    voice.Name = token.Name;
                    voice.CultureName = token.CultureName;
                    voice.Gender = String.IsNullOrEmpty(token.Gender)
                        ? "Unknown"
                        : token.Gender;
                    voice.Age = String.IsNullOrEmpty(token.Age)
                        ? "Unknown"
                        : token.Age;
                    result.Add(voice);
                }
            }
            finally
            {
                foreach (SapiVoiceToken token in native)
                {
                    SapiCom.Release(token.Token);
                }
            }
            return result;
        }

        public bool SupportsLanguage(string language)
        {
            string wanted = AssistantSettings.NormalizeLanguage(language);
            if (wanted == "auto")
            {
                return HasLanguage("zh") || HasLanguage("en");
            }
            return HasLanguage(wanted);
        }

        private bool HasLanguage(string prefix)
        {
            foreach (VoiceDescriptor voice in GetVoices())
            {
                if (voice.CultureName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public Task SpeakAsync(
            string text,
            string language,
            int rate,
            string voiceName,
            CancellationToken cancellationToken)
        {
            if (disposed)
            {
                throw new ObjectDisposedException("WindowsTtsService");
            }
            if (String.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("朗读文本为空。", "text");
            }
            lock (sync)
            {
                if (currentTask != null)
                {
                    throw new InvalidOperationException("上一段语音仍在播放，请先停止。");
                }
            }

            string resolved = ResolveLanguage(text, language);
            SapiVoiceToken token = FindVoice(resolved, voiceName);
            if (token == null)
            {
                throw new InvalidOperationException(
                    resolved == "zh"
                        ? "未找到中文 Windows 语音。请安装中文语音包。"
                        : "未找到英文 Windows 语音。请安装英文语音包。");
            }

            object voice = null;
            try
            {
                voice = SapiCom.CreateVoice();
                SapiCom.SelectVoice(voice, token.Token);
                SapiCom.SetInteger(voice, "Rate", Math.Max(-10, Math.Min(10, rate)));
                SapiCom.SetInteger(voice, "Volume", 100);
                TaskCompletionSource<object> completion =
                    new TaskCompletionSource<object>();
                CancellationTokenRegistration registration = cancellationToken.Register(
                    delegate { StopSpeaking(); });
                lock (sync)
                {
                    currentVoice = voice;
                    currentTask = completion;
                    cancellationRegistration = registration;
                    speechStartedTick = Environment.TickCount;
                    stopRequested = false;
                }
                SapiCom.Speak(voice, text, 1);
                completionTimer.Start();
                RuntimeLog.Write(
                    "TTS started. voice=" + token.Name + " culture=" + token.CultureName);
                return completion.Task;
            }
            catch (Exception exception)
            {
                lock (sync)
                {
                    currentVoice = null;
                    currentTask = null;
                    cancellationRegistration.Dispose();
                }
                SapiCom.Release(voice);
                string message =
                    "无法播放语音。请检查默认音频输出设备: " + SafeMessage(exception);
                RuntimeLog.Write("TTS failed to start: " + message);
                throw new InvalidOperationException(message);
            }
            finally
            {
                SapiCom.Release(token.Token);
            }
        }

        public void StopSpeaking()
        {
            object voice;
            TaskCompletionSource<object> completion;
            lock (sync)
            {
                voice = currentVoice;
                completion = currentTask;
                stopRequested = true;
            }
            if (voice != null)
            {
                try
                {
                    SapiCom.Speak(voice, "", 3);
                }
                catch
                {
                }
            }
            if (completion != null)
            {
                completion.TrySetCanceled();
            }
            FinishSpeech(true, null);
        }

        private void CompletionTimerTick(object sender, EventArgs e)
        {
            object voice;
            lock (sync)
            {
                voice = currentVoice;
            }
            if (voice == null)
            {
                completionTimer.Stop();
                return;
            }
            try
            {
                int elapsed = unchecked(Environment.TickCount - speechStartedTick);
                if (elapsed > 180 && SapiCom.WaitUntilDone(voice, 0))
                {
                    FinishSpeech(false, null);
                }
            }
            catch (Exception exception)
            {
                RuntimeLog.Write("TTS completion check failed: " + exception);
                FinishSpeech(false, exception);
            }
        }

        private void FinishSpeech(bool cancelled, Exception error)
        {
            object voice;
            TaskCompletionSource<object> completion;
            lock (sync)
            {
                voice = currentVoice;
                completion = currentTask;
                currentVoice = null;
                currentTask = null;
                cancellationRegistration.Dispose();
            }
            completionTimer.Stop();
            SapiCom.Release(voice);
            if (completion == null)
            {
                return;
            }
            if (error != null)
            {
                string message = "语音播报失败: " + SafeMessage(error);
                completion.TrySetException(new InvalidOperationException(message));
                EventHandler<VoiceFailureEventArgs> failed = SpeechFailed;
                if (failed != null)
                {
                    failed(this, new VoiceFailureEventArgs(message));
                }
                return;
            }
            if (cancelled || stopRequested)
            {
                completion.TrySetCanceled();
                EventHandler<VoiceCompletedEventArgs> stopped = SpeechCompleted;
                if (stopped != null)
                {
                    stopped(this, new VoiceCompletedEventArgs(true));
                }
                return;
            }
            completion.TrySetResult(null);
            EventHandler<VoiceCompletedEventArgs> completed = SpeechCompleted;
            if (completed != null)
            {
                completed(this, new VoiceCompletedEventArgs(false));
            }
        }

        private SapiVoiceToken FindVoice(string language, string preferredName)
        {
            List<SapiVoiceToken> voices = SapiCom.DescribeVoices();
            SapiVoiceToken selected = null;
            if (!String.IsNullOrWhiteSpace(preferredName))
            {
                foreach (SapiVoiceToken token in voices)
                {
                    bool languageMatches = token.CultureName.StartsWith(
                        language,
                        StringComparison.OrdinalIgnoreCase);
                    bool nameMatches = String.Equals(
                        token.Name,
                        preferredName,
                        StringComparison.OrdinalIgnoreCase);
                    if (languageMatches && nameMatches)
                    {
                        selected = token;
                        break;
                    }
                    SapiCom.Release(token.Token);
                }
                if (selected != null)
                {
                    foreach (SapiVoiceToken token in voices)
                    {
                        if (!ReferenceEquals(token, selected) && token.Token != null)
                        {
                            SapiCom.Release(token.Token);
                            token.Token = null;
                        }
                    }
                    return selected;
                }
                voices = SapiCom.DescribeVoices();
            }
            foreach (SapiVoiceToken token in voices)
            {
                if (!token.CultureName.StartsWith(
                    language,
                    StringComparison.OrdinalIgnoreCase))
                {
                    SapiCom.Release(token.Token);
                    continue;
                }
                if (selected == null ||
                    String.Equals(token.Gender, "Male", StringComparison.OrdinalIgnoreCase))
                {
                    if (selected != null)
                    {
                        SapiCom.Release(selected.Token);
                    }
                    selected = token;
                }
                else
                {
                    SapiCom.Release(token.Token);
                }
            }
            return selected;
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

        private static string SafeMessage(Exception exception)
        {
            string message = exception.Message ?? "unknown error";
            return message.Length > 180 ? message.Substring(0, 180) : message;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            StopSpeaking();
            completionTimer.Stop();
        }
    }

    internal sealed class CloudTtsService : IVoiceOutputService
    {
        public event EventHandler<VoiceCompletedEventArgs> SpeechCompleted
        {
            add { }
            remove { }
        }

        public event EventHandler<VoiceFailureEventArgs> SpeechFailed
        {
            add { }
            remove { }
        }

        public string Id
        {
            get { return "cloud"; }
        }

        public string DisplayName
        {
            get { return "ElevenLabs 云端语音（预留）"; }
        }

        public bool IsAvailable
        {
            get { return false; }
        }

        public string AvailabilityMessage
        {
            get
            {
                return "云端 TTS 接口已预留，但本版本不会自动启用付费服务。" +
                    "请先确认 ElevenLabs 官方接口、费用、声音授权和中英文模型后，" +
                    "再配置 ELEVENLABS_API_KEY 并选择云端语音。";
            }
        }

        public IList<VoiceDescriptor> GetVoices()
        {
            return new List<VoiceDescriptor>();
        }

        public bool SupportsLanguage(string language)
        {
            return false;
        }

        public Task SpeakAsync(
            string text,
            string language,
            int rate,
            string voiceName,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException(AvailabilityMessage);
        }

        public void StopSpeaking()
        {
        }

        public void Dispose()
        {
        }
    }

    internal static class VoiceDiagnostics
    {
        public static string Run(string projectRoot)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("VOICE DIAGNOSTICS");
            WindowsSpeechRecognitionService recognition =
                new WindowsSpeechRecognitionService();
            try
            {
                IList<RecognizerDescriptor> recognizers =
                    recognition.GetInstalledRecognizers();
                report.AppendLine("SpeechRecognizers=" + recognizers.Count);
                foreach (RecognizerDescriptor item in recognizers)
                {
                    report.AppendLine(
                        "Recognizer=" + item.CultureName + " | " + item.Name);
                }
                bool hasZh = false;
                bool hasEn = false;
                foreach (RecognizerDescriptor item in recognizers)
                {
                    hasZh |= item.CultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
                    hasEn |= item.CultureName.StartsWith("en", StringComparison.OrdinalIgnoreCase);
                }
                report.AppendLine(hasZh ? "ChineseRecognition=INSTALLED" :
                    "ChineseRecognition=NOT_INSTALLED");
                report.AppendLine(hasEn ? "EnglishRecognition=INSTALLED" :
                    "EnglishRecognition=NOT_INSTALLED");
                report.AppendLine("Microphone=" +
                    WindowsSpeechRecognitionService.TestMicrophoneAvailability());
            }
            finally
            {
                recognition.Dispose();
            }

            try
            {
                List<SapiVoiceToken> voices = SapiCom.DescribeVoices();
                report.AppendLine("TtsVoices=" + voices.Count);
                bool hasZh = false;
                bool hasEn = false;
                foreach (SapiVoiceToken voice in voices)
                {
                    report.AppendLine(
                        "Voice=" + voice.Name + " | " + voice.CultureName +
                        " | " + voice.Gender + " | " + voice.Age);
                    hasZh |= voice.CultureName.StartsWith(
                        "zh", StringComparison.OrdinalIgnoreCase);
                    hasEn |= voice.CultureName.StartsWith(
                        "en", StringComparison.OrdinalIgnoreCase);
                    SapiCom.Release(voice.Token);
                }
                report.AppendLine(hasZh ? "ChineseTtsVoice=INSTALLED" :
                    "ChineseTtsVoice=NOT_INSTALLED");
                report.AppendLine(hasEn ? "EnglishTtsVoice=INSTALLED" :
                    "EnglishTtsVoice=NOT_INSTALLED");
                report.AppendLine("ChineseTtsPlayback=" + TestSapiPlayback("zh"));
                report.AppendLine("EnglishTtsPlayback=" + TestSapiPlayback("en"));
            }
            catch (Exception exception)
            {
                report.AppendLine("TtsDiagnostics=FAIL: " + exception.Message);
            }
            try
            {
                using (SpeechSynthesizer systemSpeech = new SpeechSynthesizer())
                {
                    int count = 0;
                    foreach (InstalledVoice voice in systemSpeech.GetInstalledVoices())
                    {
                        VoiceInfo info = voice.VoiceInfo;
                        count++;
                        report.AppendLine(
                            "SystemSpeechVoice=" + info.Name + " | " +
                            info.Culture.Name + " | " + info.Gender +
                            " | enabled=" + voice.Enabled);
                    }
                    report.AppendLine("SystemSpeechVoiceCount=" + count);
                }
            }
            catch (Exception exception)
            {
                report.AppendLine(
                    "SystemSpeechEnumeration=UNAVAILABLE: " + ShortError(exception));
            }
            ESpeakTtsService eSpeak = new ESpeakTtsService();
            try
            {
                report.AppendLine("ESpeakAvailable=" + (eSpeak.IsAvailable ? "True" : "False"));
                foreach (VoiceDescriptor voice in eSpeak.GetVoices())
                {
                    report.AppendLine(
                        "ESpeakVoice=" + voice.Name + " | " +
                        voice.CultureName + " | " + voice.Gender +
                        " | id=" + voice.VoiceId);
                }
            }
            finally
            {
                eSpeak.Dispose();
            }
            string key = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
            report.AppendLine("DeepSeekKeyConfigured=" +
                (String.IsNullOrWhiteSpace(key) ? "False" : "True"));
            string cloudKey = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
            report.AppendLine("ElevenLabsKeyConfigured=" +
                (String.IsNullOrWhiteSpace(cloudKey) ? "False" : "True"));
            report.AppendLine("CloudTtsEnabled=False");
            return report.ToString();
        }

        private static string TestSapiPlayback(string language)
        {
            object voice = null;
            object collection = null;
            object selectedToken = null;
            try
            {
                voice = SapiCom.CreateVoice();
                collection = SapiCom.GetVoices(voice);
                int count = SapiCom.Count(collection);
                for (int index = 0; index < count; index++)
                {
                    object token = SapiCom.Item(collection, index);
                    string culture = SapiCom.Attribute(token, "Language");
                    if (culture.StartsWith("804", StringComparison.OrdinalIgnoreCase) &&
                        language == "zh")
                    {
                        selectedToken = token;
                        break;
                    }
                    if (culture.StartsWith("409", StringComparison.OrdinalIgnoreCase) &&
                        language == "en")
                    {
                        selectedToken = token;
                        break;
                    }
                    SapiCom.Release(token);
                }
                if (selectedToken == null)
                {
                    return "UNAVAILABLE: missing voice";
                }
                SapiCom.SelectVoice(voice, selectedToken);
                SapiCom.SetInteger(voice, "Volume", 0);
                SapiCom.Speak(voice, "Test.", 0);
                return "PASS: synthesis completed with volume 0";
            }
            catch (Exception exception)
            {
                string message = ShortError(exception);
                if (message.IndexOf("拒绝访问", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("access", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "UNAVAILABLE: default audio output is unavailable or access denied";
                }
                return "UNAVAILABLE: " + message;
            }
            finally
            {
                SapiCom.Release(selectedToken);
                SapiCom.Release(collection);
                SapiCom.Release(voice);
            }
        }

        private static string ShortError(Exception exception)
        {
            string message = exception.Message ?? "unknown error";
            return message.Length > 160 ? message.Substring(0, 160) : message;
        }
    }
}
