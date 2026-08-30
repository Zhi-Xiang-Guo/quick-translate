using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace QuickTranslate
{
    internal sealed class TranslationClient
    {
        private const string Instructions =
            "Translate the user's Chinese text into natural, concise English. " +
            "Preserve meaning, tone, paragraph breaks, Markdown, names, numbers, URLs, and code fragments. " +
            "Do not explain, annotate, quote, or wrap the translation. Output only the translated English text.";

        public async Task<string> TranslateAsync(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                throw new InvalidOperationException("没有可翻译的文本");
            }
            if (source.Length > 20000)
            {
                throw new InvalidOperationException("选中文本超过 20000 个字符，请分段翻译");
            }

            CcSwitchConfig config = CcSwitchConfig.LoadCurrent();
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 1024 * 1024 * 4;

            Dictionary<string, object> body = new Dictionary<string, object>();
            body["model"] = config.Model;
            if (string.Equals(config.WireApi, "responses", StringComparison.OrdinalIgnoreCase))
            {
                body["instructions"] = Instructions;
                body["input"] = source;
                body["reasoning"] = new Dictionary<string, object> { { "effort", "low" } };
                body["max_output_tokens"] = 4000;
                body["store"] = false;
            }
            else
            {
                body["messages"] = new object[]
                {
                    new Dictionary<string, object> { { "role", "system" }, { "content", Instructions } },
                    new Dictionary<string, object> { { "role", "user" }, { "content", source } }
                };
                body["reasoning_effort"] = "low";
                body["max_tokens"] = 4000;
                body["stream"] = false;
            }

            DiagnosticLog.Write("API request; model=" + config.Model + "; wireApi=" + config.WireApi +
                "; reasoning=low");

            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (HttpClientHandler handler = new HttpClientHandler())
            using (HttpClient client = new HttpClient(handler))
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, config.Endpoint))
            {
                handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                client.Timeout = TimeSpan.FromSeconds(90);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(serializer.Serialize(body), Encoding.UTF8, "application/json");

                using (HttpResponseMessage response = await client.SendAsync(request).ConfigureAwait(false))
                {
                    string responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        string message = ExtractError(serializer, responseText);
                        throw new InvalidOperationException(string.Format("接口返回 HTTP {0}：{1}",
                            (int)response.StatusCode, message));
                    }

                    string translated = ExtractText(serializer, responseText, config.WireApi);
                    if (string.IsNullOrWhiteSpace(translated))
                    {
                        throw new InvalidOperationException("接口返回成功，但没有找到译文");
                    }
                    return CleanOutput(translated);
                }
            }
        }

        private static string ExtractText(JavaScriptSerializer serializer, string json, string wireApi)
        {
            object rootObject = serializer.DeserializeObject(json);
            Dictionary<string, object> root = rootObject as Dictionary<string, object>;
            if (root == null) return null;

            object direct;
            if (root.TryGetValue("output_text", out direct) && direct is string)
            {
                return (string)direct;
            }

            if (string.Equals(wireApi, "responses", StringComparison.OrdinalIgnoreCase))
            {
                object outputObject;
                if (!root.TryGetValue("output", out outputObject)) return null;
                IEnumerable output = outputObject as IEnumerable;
                if (output == null) return null;

                StringBuilder builder = new StringBuilder();
                foreach (object itemObject in output)
                {
                    Dictionary<string, object> item = itemObject as Dictionary<string, object>;
                    if (item == null) continue;
                    object contentObject;
                    if (!item.TryGetValue("content", out contentObject)) continue;
                    IEnumerable content = contentObject as IEnumerable;
                    if (content == null) continue;
                    foreach (object partObject in content)
                    {
                        Dictionary<string, object> part = partObject as Dictionary<string, object>;
                        if (part == null) continue;
                        object textObject;
                        if (part.TryGetValue("text", out textObject) && textObject is string)
                        {
                            builder.Append((string)textObject);
                        }
                    }
                }
                return builder.ToString();
            }

            object choicesObject;
            if (!root.TryGetValue("choices", out choicesObject)) return null;
            object[] choices = choicesObject as object[];
            if (choices == null || choices.Length == 0) return null;
            Dictionary<string, object> choice = choices[0] as Dictionary<string, object>;
            object messageObject;
            if (choice == null || !choice.TryGetValue("message", out messageObject)) return null;
            Dictionary<string, object> message = messageObject as Dictionary<string, object>;
            object contentValue;
            return message != null && message.TryGetValue("content", out contentValue)
                ? Convert.ToString(contentValue)
                : null;
        }

        private static string ExtractError(JavaScriptSerializer serializer, string json)
        {
            try
            {
                Dictionary<string, object> root = serializer.Deserialize<Dictionary<string, object>>(json);
                object errorObject;
                if (root != null && root.TryGetValue("error", out errorObject))
                {
                    Dictionary<string, object> error = errorObject as Dictionary<string, object>;
                    object message;
                    if (error != null && error.TryGetValue("message", out message))
                    {
                        return Limit(Convert.ToString(message));
                    }
                    return Limit(Convert.ToString(errorObject));
                }
            }
            catch
            {
            }
            return Limit(json);
        }

        private static string Limit(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "无错误详情";
            value = value.Trim();
            return value.Length <= 400 ? value : value.Substring(0, 400) + "...";
        }

        private static string CleanOutput(string value)
        {
            value = value.Trim();
            if (value.Length >= 6 && value.StartsWith("```", StringComparison.Ordinal) &&
                value.EndsWith("```", StringComparison.Ordinal))
            {
                int firstLine = value.IndexOf('\n');
                if (firstLine >= 0) value = value.Substring(firstLine + 1, value.Length - firstLine - 4).Trim();
            }
            return value;
        }
    }
}
