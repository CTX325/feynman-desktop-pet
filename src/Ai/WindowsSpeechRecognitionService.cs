using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Speech.Recognition;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FeynmanPet
{
    internal sealed class SpeechRecognitionStartResult
    {
        public bool Success;
        public string Message;
        public string CultureName;
    }

    internal sealed class RecognizerDescriptor
    {
        public string Name;
        public string CultureName;
        public string Description;
    }

    internal sealed class WindowsSpeechRecognitionService : IDisposable
    {
        private readonly object sync = new object();
        private readonly StringBuilder recognizedText = new StringBuilder();
        private SpeechRecognitionEngine engine;
        private TaskCompletionSource<string> stopCompletion;
        private bool listening;
        private bool cancelled;
        private bool stopRequested;
        private bool disposed;

        public event EventHandler<SpeechRecognitionErrorEventArgs> RecognitionFailed;
        public event EventHandler<SpeechRecognitionResultEventArgs> RecognitionFinished;

        public bool IsListening
        {
            get { lock (sync) { return listening; } }
        }

        public string RecognitionLanguage
        {
            get
            {
                lock (sync)
                {
                    return engine == null ? null : engine.RecognizerInfo.Culture.Name;
                }
            }
        }

        public IList<RecognizerDescriptor> GetInstalledRecognizers()
        {
            List<RecognizerDescriptor> result = new List<RecognizerDescriptor>();
            try
            {
                foreach (RecognizerInfo info in SpeechRecognitionEngine.InstalledRecognizers())
                {
                    RecognizerDescriptor item = new RecognizerDescriptor();
                    item.Name = info.Name;
                    item.CultureName = info.Culture.Name;
                    item.Description = info.Description;
                    result.Add(item);
                }
            }
            catch (Exception exception)
            {
                RuntimeLog.Write("Speech recognizer enumeration failed: " + exception.Message);
            }
            return result;
        }

        public bool SupportsLanguage(string language)
        {
            string preferred = AssistantSettings.NormalizeLanguage(language);
            foreach (RecognizerDescriptor item in GetInstalledRecognizers())
            {
                if (preferred == "auto" ||
                    (preferred == "zh" &&
                        item.CultureName.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) ||
                    (preferred == "en" &&
                        item.CultureName.StartsWith("en", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }
            return false;
        }

        public SpeechRecognitionStartResult Start(string language)
        {
            SpeechRecognitionStartResult result = new SpeechRecognitionStartResult();
            if (disposed)
            {
                result.Message = "语音识别服务已经关闭。";
                return result;
            }
            lock (sync)
            {
                if (listening)
                {
                    result.Success = true;
                    result.Message = "录音已经在进行中。";
                    result.CultureName = RecognitionLanguage;
                    return result;
                }
            }

            RecognizerInfo recognizer;
            string resolutionMessage;
            if (!TrySelectRecognizer(language, out recognizer, out resolutionMessage))
            {
                result.Message = resolutionMessage;
                return result;
            }

            ResetEngine();
            try
            {
                SpeechRecognitionEngine newEngine = new SpeechRecognitionEngine(recognizer);
                newEngine.LoadGrammar(new DictationGrammar());
                newEngine.BabbleTimeout = TimeSpan.FromSeconds(4);
                newEngine.InitialSilenceTimeout = TimeSpan.FromSeconds(10);
                newEngine.EndSilenceTimeout = TimeSpan.FromMilliseconds(850);
                newEngine.EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(1.2);
                newEngine.SpeechRecognized += SpeechRecognized;
                newEngine.RecognizeCompleted += RecognizeCompleted;
                lock (sync)
                {
                    engine = newEngine;
                }
                newEngine.SetInputToDefaultAudioDevice();

                lock (sync)
                {
                    recognizedText.Length = 0;
                    cancelled = false;
                    stopRequested = false;
                    stopCompletion = null;
                    listening = true;
                }
                newEngine.RecognizeAsync(RecognizeMode.Multiple);
                result.Success = true;
                result.CultureName = recognizer.Culture.Name;
                result.Message = "正在使用 " + recognizer.Culture.DisplayName + " 录音。";
                RuntimeLog.Write("Speech recognition started. culture=" + recognizer.Culture.Name);
                return result;
            }
            catch (InvalidOperationException exception)
            {
                result.Message = FriendlyStartError(exception);
            }
            catch (COMException exception)
            {
                result.Message = FriendlyStartError(exception);
            }
            catch (UnauthorizedAccessException exception)
            {
                result.Message = FriendlyStartError(exception);
            }
            catch (ArgumentException exception)
            {
                result.Message = FriendlyStartError(exception);
            }
            catch (Exception exception)
            {
                result.Message = "无法打开麦克风: " + exception.Message;
            }
            ResetEngine();
            return result;
        }

        public async Task<string> StopAsync(CancellationToken cancellationToken)
        {
            TaskCompletionSource<string> completion;
            SpeechRecognitionEngine current;
            lock (sync)
            {
                if (!listening || engine == null)
                {
                    return recognizedText.ToString().Trim();
                }
                current = engine;
                stopRequested = true;
                if (stopCompletion == null)
                {
                    stopCompletion = new TaskCompletionSource<string>();
                }
                completion = stopCompletion;
            }
            try
            {
                current.RecognizeAsyncStop();
            }
            catch (Exception exception)
            {
                RuntimeLog.Write("Speech recognition stop failed: " + exception.Message);
                lock (sync)
                {
                    listening = false;
                }
                ResetEngine();
                return recognizedText.ToString().Trim();
            }

            Task delay = Task.Delay(5000, cancellationToken);
            Task winner = await Task.WhenAny(completion.Task, delay);
            if (winner != completion.Task)
            {
                Cancel();
                throw new TimeoutException("语音识别没有及时结束，请重试。");
            }
            string text = await completion.Task;
            ResetEngine();
            return (text ?? "").Trim();
        }

        public void Cancel()
        {
            SpeechRecognitionEngine current;
            lock (sync)
            {
                current = engine;
                cancelled = true;
                listening = false;
                if (stopCompletion != null)
                {
                    stopCompletion.TrySetCanceled();
                    stopCompletion = null;
                }
            }
            if (current != null)
            {
                try
                {
                    current.RecognizeAsyncCancel();
                }
                catch
                {
                }
            }
            ResetEngine();
            RuntimeLog.Write("Speech recognition cancelled.");
        }

        private bool TrySelectRecognizer(
            string language,
            out RecognizerInfo selected,
            out string message)
        {
            selected = null;
            message = "";
            string preferred = AssistantSettings.NormalizeLanguage(language);
            IList<RecognizerDescriptor> installed;
            try
            {
                installed = GetInstalledRecognizers();
            }
            catch (Exception exception)
            {
                message = "无法读取 Windows 语音识别器: " + exception.Message;
                return false;
            }
            if (installed.Count == 0)
            {
                message =
                    "Windows 没有安装语音识别语言包。请打开“设置 → 时间和语言 → " +
                    "语言和区域”，为中文或英文安装语音识别组件。";
                return false;
            }

            string wanted = preferred;
            if (wanted == "auto")
            {
                string ui = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                wanted = ui == "zh" ? "zh" : (ui == "en" ? "en" : "zh");
            }
            foreach (RecognizerInfo info in SpeechRecognitionEngine.InstalledRecognizers())
            {
                if (info.Culture.Name.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    selected = info;
                    if (preferred == "auto")
                    {
                        message = "自动选择 Windows 识别语言: " + info.Culture.DisplayName +
                            "。如需另一种语言，请安装对应语言包并手动切换。";
                    }
                    else
                    {
                        message = "使用 Windows 识别语言: " + info.Culture.DisplayName;
                    }
                    return true;
                }
            }

            if (wanted == "en")
            {
                message =
                    "当前 Windows 只检测到中文语音识别器，没有英文识别语言包。" +
                    "请在“设置 → 时间和语言 → 语言和区域”中为 English 安装语音识别组件后重试。";
            }
            else
            {
                message =
                    "当前 Windows 没有可用的中文语音识别器。" +
                    "请在“设置 → 时间和语言 → 语言和区域”中安装中文语音识别组件后重试。";
            }
            return false;
        }

        private void SpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            if (e.Result == null || String.IsNullOrWhiteSpace(e.Result.Text))
            {
                return;
            }
            lock (sync)
            {
                if (recognizedText.Length > 0)
                {
                    char previous = recognizedText[recognizedText.Length - 1];
                    char next = e.Result.Text[0];
                    if ((previous < 0x2E80 && next < 0x2E80) &&
                        previous != ' ' && next != ' ')
                    {
                        recognizedText.Append(' ');
                    }
                }
                recognizedText.Append(e.Result.Text.Trim());
            }
        }

        private void RecognizeCompleted(object sender, RecognizeCompletedEventArgs e)
        {
            string text;
            Exception error = e.Error;
            bool wasCancelled;
            bool requestedStop;
            TaskCompletionSource<string> completion;
            lock (sync)
            {
                text = recognizedText.ToString().Trim();
                wasCancelled = cancelled;
                requestedStop = stopRequested;
                listening = false;
                completion = stopCompletion;
                stopCompletion = null;
            }

            if (error != null && !wasCancelled)
            {
                RuntimeLog.Write("Speech recognition error: " + error.Message);
                EventHandler<SpeechRecognitionErrorEventArgs> handler = RecognitionFailed;
                if (handler != null)
                {
                    handler(
                        this,
                        new SpeechRecognitionErrorEventArgs(
                            "语音识别失败: " + SafeSpeechError(error)));
                }
                if (completion != null)
                {
                    completion.TrySetException(
                        new InvalidOperationException("语音识别失败: " + SafeSpeechError(error)));
                }
                return;
            }
            if (wasCancelled)
            {
                if (completion != null)
                {
                    completion.TrySetCanceled();
                }
                return;
            }
            if (completion != null)
            {
                completion.TrySetResult(text);
                RuntimeLog.Write(
                    "Speech recognition completed. textLength=" + text.Length +
                    " requestedStop=" + requestedStop);
            }
            else
            {
                EventHandler<SpeechRecognitionResultEventArgs> handler = RecognitionFinished;
                if (handler != null)
                {
                    RuntimeLog.Write(
                        "Speech recognition auto-completed. textLength=" + text.Length);
                    handler(this, new SpeechRecognitionResultEventArgs(text));
                }
            }
        }

        private static string FriendlyStartError(Exception exception)
        {
            string message = exception.Message ?? "";
            if (message.IndexOf("audio", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("microphone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("device", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("麦克风", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("识别器", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf(" ID", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "无法访问默认麦克风。请检查麦克风连接和 Windows 麦克风隐私权限。";
            }
            return "无法开始录音: " + message;
        }

        private static string SafeSpeechError(Exception exception)
        {
            string message = exception.Message ?? "未知错误";
            return message.Length > 160 ? message.Substring(0, 160) : message;
        }

        private void ResetEngine()
        {
            SpeechRecognitionEngine old = null;
            lock (sync)
            {
                old = engine;
                engine = null;
                listening = false;
                stopRequested = false;
            }
            if (old != null)
            {
                try
                {
                    old.SpeechRecognized -= SpeechRecognized;
                    old.RecognizeCompleted -= RecognizeCompleted;
                    old.SetInputToNull();
                }
                catch
                {
                }
                old.Dispose();
            }
        }

        public static string TestMicrophoneAvailability()
        {
            try
            {
                IList<RecognizerInfo> recognizers = SpeechRecognitionEngine.InstalledRecognizers();
                if (recognizers.Count == 0)
                {
                    return "UNAVAILABLE: no Windows speech recognizer is installed";
                }
                using (SpeechRecognitionEngine test = new SpeechRecognitionEngine(recognizers[0]))
                {
                    test.SetInputToDefaultAudioDevice();
                    test.SetInputToNull();
                }
                return "PASS: default microphone can be opened";
            }
            catch (Exception exception)
            {
                return "UNAVAILABLE: " + SafeSpeechError(exception);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            Cancel();
        }
    }

    internal sealed class SpeechRecognitionErrorEventArgs : EventArgs
    {
        public readonly string Message;

        public SpeechRecognitionErrorEventArgs(string message)
        {
            Message = message;
        }
    }

    internal sealed class SpeechRecognitionResultEventArgs : EventArgs
    {
        public readonly string Text;

        public SpeechRecognitionResultEventArgs(string text)
        {
            Text = text ?? "";
        }
    }
}
