using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FeynmanPet
{
    internal sealed class ChatServiceException : Exception
    {
        public string Code;

        public ChatServiceException(string code, string message)
            : base(message)
        {
            Code = code;
        }
    }

#pragma warning disable 0649
    [DataContract]
    internal sealed class DeepSeekMessage
    {
        [DataMember(Name = "role", Order = 1)]
        public string Role;

        [DataMember(Name = "content", Order = 2)]
        public string Content;

        public DeepSeekMessage()
        {
        }

        public DeepSeekMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }

    [DataContract]
    internal sealed class DeepSeekRequest
    {
        [DataMember(Name = "model", Order = 1)]
        public string Model;

        [DataMember(Name = "messages", Order = 2)]
        public DeepSeekMessage[] Messages;

        [DataMember(Name = "stream", Order = 3)]
        public bool Stream;

        [DataMember(Name = "temperature", Order = 4)]
        public double Temperature;

        [DataMember(Name = "max_tokens", Order = 5)]
        public int MaxTokens;
    }

    [DataContract]
    internal sealed class DeepSeekResponse
    {
        [DataMember(Name = "choices")]
        public DeepSeekChoice[] Choices;

        [DataMember(Name = "error")]
        public DeepSeekApiError Error;
    }

    [DataContract]
    internal sealed class DeepSeekChoice
    {
        [DataMember(Name = "message")]
        public DeepSeekMessage Message;

        [DataMember(Name = "finish_reason")]
        public string FinishReason;
    }

    [DataContract]
    internal sealed class DeepSeekApiError
    {
        [DataMember(Name = "message")]
        public string Message;

        [DataMember(Name = "type")]
        public string Type;

        [DataMember(Name = "code")]
        public string Code;
    }
#pragma warning restore 0649

    internal sealed class DeepSeekChatService : IDisposable
    {
        public const string ApiBase = "https://api.deepseek.com";
        public const string ChatPath = "/chat/completions";
        public const int MaxHistoryMessages = 8;

        private readonly HttpClient client;
        private readonly List<DeepSeekMessage> history = new List<DeepSeekMessage>();
        private int requestInFlight;
        private bool disposed;

        public DeepSeekChatService()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
            }
            client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(70);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FeynmanDesktopPet/2.0");
        }

        public bool IsConfigured
        {
            get { return !String.IsNullOrWhiteSpace(GetApiKey()); }
        }

        public string ConfigurationHint
        {
            get
            {
                if (IsConfigured)
                {
                    return "DeepSeek API Key 已配置（来自环境变量，不在设置文件中保存）。";
                }
                return "未检测到 DEEPSEEK_API_KEY。请先设置系统环境变量并重新启动桌宠。";
            }
        }

        public int HistoryCount
        {
            get { return history.Count; }
        }

        public void ClearHistory()
        {
            history.Clear();
        }

        public async Task<string> AskAsync(
            string userText,
            AssistantSettings settings,
            string answerLanguage,
            CancellationToken cancellationToken)
        {
            if (disposed)
            {
                throw new ObjectDisposedException("DeepSeekChatService");
            }
            if (String.IsNullOrWhiteSpace(userText))
            {
                throw new ChatServiceException("empty", "问题内容为空。");
            }
            string key = GetApiKey();
            if (String.IsNullOrEmpty(key))
            {
                throw new ChatServiceException(
                    "missing_key",
                    "未配置 DEEPSEEK_API_KEY。请设置环境变量并重新启动桌宠后再试。");
            }
            if (Interlocked.CompareExchange(ref requestInFlight, 1, 0) != 0)
            {
                throw new ChatServiceException("busy", "上一条回答仍在生成，请先等待或取消。");
            }
            try
            {
                List<DeepSeekMessage> messages = new List<DeepSeekMessage>();
                messages.Add(new DeepSeekMessage(
                    "system",
                    BuildSystemPrompt(settings, answerLanguage)));
                int start = Math.Max(0, history.Count - MaxHistoryMessages);
                for (int index = start; index < history.Count; index++)
                {
                    messages.Add(history[index]);
                }
                messages.Add(new DeepSeekMessage("user", userText.Trim()));

                DeepSeekRequest request = new DeepSeekRequest();
                request.Model = String.IsNullOrWhiteSpace(settings.DeepSeekModel)
                    ? "deepseek-chat"
                    : settings.DeepSeekModel.Trim();
                request.Messages = messages.ToArray();
                request.Stream = false;
                request.Temperature = 0.65;
                request.MaxTokens = 900;

                string body = Serialize(request);
                using (HttpRequestMessage httpRequest = new HttpRequestMessage(
                    HttpMethod.Post,
                    ApiBase + ChatPath))
                {
                    httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                    httpRequest.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    using (CancellationTokenSource timeout =
                        CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(60));
                        HttpResponseMessage response;
                        try
                        {
                            response = await client.SendAsync(
                                httpRequest,
                                HttpCompletionOption.ResponseContentRead,
                                timeout.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                throw;
                            }
                            throw new ChatServiceException(
                                "timeout",
                                "DeepSeek 请求超时。请检查网络后重试。");
                        }
                        catch (HttpRequestException exception)
                        {
                            throw new ChatServiceException(
                                "network",
                                "无法连接 DeepSeek，请检查网络连接。" +
                                " (" + Sanitize(exception.Message, key) + ")");
                        }

                        using (response)
                        {
                            string responseText = await response.Content.ReadAsStringAsync();
                            if (!response.IsSuccessStatusCode)
                            {
                                throw BuildHttpError(response.StatusCode, responseText, key);
                            }
                            DeepSeekResponse parsed = DeserializeResponse(responseText);
                            if (parsed == null || parsed.Choices == null ||
                                parsed.Choices.Length == 0 ||
                                parsed.Choices[0] == null ||
                                parsed.Choices[0].Message == null ||
                                String.IsNullOrWhiteSpace(parsed.Choices[0].Message.Content))
                            {
                                throw new ChatServiceException(
                                    "invalid_response",
                                    "DeepSeek 返回了无法解析的回答。请稍后重试。");
                            }
                            string answer = parsed.Choices[0].Message.Content.Trim();
                            history.Add(new DeepSeekMessage("user", userText.Trim()));
                            history.Add(new DeepSeekMessage("assistant", answer));
                            while (history.Count > MaxHistoryMessages)
                            {
                                history.RemoveAt(0);
                            }
                            return answer;
                        }
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref requestInFlight, 0);
            }
        }

        internal static string BuildSystemPrompt(
            AssistantSettings settings,
            string answerLanguage)
        {
            string prompt = String.IsNullOrWhiteSpace(settings.SystemPrompt)
                ? AssistantSettings.DefaultPrompt
                : settings.SystemPrompt.Trim();
            string language = AssistantSettings.NormalizeLanguage(answerLanguage);
            string requirement;
            if (language == "zh")
            {
                requirement =
                    "Always write every ordinary conversational reply in natural Simplified Chinese " +
                    "(简体中文), even when the latest user message is written in English or another " +
                    "language. Do not switch back to the user's language.";
            }
            else if (language == "en")
            {
                requirement =
                    "Always write every ordinary conversational reply in natural, fluent English, " +
                    "even when the latest user message is written in Chinese or another language. " +
                    "Do not switch back to the user's language.";
            }
            else
            {
                requirement =
                    "Use the primary language of the latest user message for this reply.";
            }
            return prompt +
                "\n\nANSWER-LANGUAGE CONTROL — HIGHEST PRIORITY:\n" +
                requirement +
                " This rule overrides any conflicting language instruction in the role prompt " +
                "or earlier conversation turns. Exception: if the latest user message explicitly " +
                "requests a translation, a quotation in another language, or output in a named " +
                "language, follow that specific request for the relevant part of the answer. " +
                "Do not mention this internal language control.";
        }

        private static string Serialize(DeepSeekRequest request)
        {
            DataContractJsonSerializer serializer =
                new DataContractJsonSerializer(typeof(DeepSeekRequest));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.WriteObject(stream, request);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static DeepSeekResponse DeserializeResponse(string json)
        {
            try
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(DeepSeekResponse));
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    return serializer.ReadObject(stream) as DeepSeekResponse;
                }
            }
            catch
            {
                return null;
            }
        }

        private ChatServiceException BuildHttpError(
            HttpStatusCode status,
            string responseText,
            string key)
        {
            string providerMessage = "";
            try
            {
                DataContractJsonSerializer serializer =
                    new DataContractJsonSerializer(typeof(DeepSeekResponse));
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(responseText)))
                {
                    DeepSeekResponse parsed = serializer.ReadObject(stream) as DeepSeekResponse;
                    if (parsed != null && parsed.Error != null)
                    {
                        providerMessage = parsed.Error.Message ?? "";
                    }
                }
            }
            catch
            {
            }
            string detail = String.IsNullOrEmpty(providerMessage)
                ? ""
                : " " + Sanitize(providerMessage, key);
            int code = (int)status;
            if (status == HttpStatusCode.Unauthorized)
            {
                return new ChatServiceException(
                    "auth",
                    "DeepSeek 鉴权失败，请检查 DEEPSEEK_API_KEY 是否正确。" + detail);
            }
            if (code == 402)
            {
                return new ChatServiceException(
                    "balance",
                    "DeepSeek 账户余额不足或额度不可用。" + detail);
            }
            if ((int)status == 429)
            {
                return new ChatServiceException(
                    "rate_limit",
                    "DeepSeek 请求过于频繁，请稍后再试。" + detail);
            }
            if (code >= 500)
            {
                return new ChatServiceException(
                    "server",
                    "DeepSeek 服务暂时不可用，请稍后重试。" + detail);
            }
            return new ChatServiceException(
                "http",
                "DeepSeek 请求失败（HTTP " + code + "）。" + detail);
        }

        private static string Sanitize(string text, string key)
        {
            if (String.IsNullOrEmpty(text))
            {
                return "";
            }
            string safe = text;
            if (!String.IsNullOrEmpty(key))
            {
                safe = safe.Replace(key, "[redacted]");
            }
            safe = safe.Replace("\r", " ").Replace("\n", " ");
            if (safe.Length > 240)
            {
                safe = safe.Substring(0, 240) + "...";
            }
            return safe.Trim();
        }

        private static string GetApiKey()
        {
            string value = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
            return String.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        public void Dispose()
        {
            disposed = true;
            client.Dispose();
        }
    }
}
