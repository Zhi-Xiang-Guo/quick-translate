using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace QuickTranslate
{
    internal sealed class CcSwitchConfig
    {
        public string ProviderName { get; private set; }
        public string Model { get; private set; }
        public string BaseUrl { get; private set; }
        public string WireApi { get; private set; }
        public string ApiKey { get; private set; }

        public string Endpoint
        {
            get
            {
                string suffix = string.Equals(WireApi, "responses", StringComparison.OrdinalIgnoreCase)
                    ? "responses"
                    : "chat/completions";
                string value = BaseUrl.TrimEnd('/');
                if (value.EndsWith("/" + suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
                return value + "/" + suffix;
            }
        }

        public static CcSwitchConfig LoadCurrent()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string configPath = Path.Combine(home, ".codex", "config.toml");
            string authPath = Path.Combine(home, ".codex", "auth.json");

            if (!File.Exists(configPath))
            {
                throw new InvalidOperationException("未找到 CC Switch 写入的 Codex 配置：" + configPath);
            }
            if (!File.Exists(authPath))
            {
                throw new InvalidOperationException("未找到 Codex API 认证文件：" + authPath);
            }

            Dictionary<string, string> global = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Dictionary<string, string>> providers =
                new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> current = global;

            Regex sectionPattern = new Regex(@"^\s*\[model_providers\.([^\]]+)\]\s*$");
            Regex valuePattern = new Regex("^\\s*([A-Za-z0-9_]+)\\s*=\\s*\"((?:\\\\.|[^\"])*)\"\\s*(?:#.*)?$");

            foreach (string rawLine in File.ReadAllLines(configPath))
            {
                Match sectionMatch = sectionPattern.Match(rawLine);
                if (sectionMatch.Success)
                {
                    string name = sectionMatch.Groups[1].Value.Trim();
                    if (!providers.TryGetValue(name, out current))
                    {
                        current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        providers[name] = current;
                    }
                    continue;
                }

                if (rawLine.TrimStart().StartsWith("[", StringComparison.Ordinal))
                {
                    current = null;
                    continue;
                }

                Match valueMatch = valuePattern.Match(rawLine);
                if (current != null && valueMatch.Success)
                {
                    current[valueMatch.Groups[1].Value] = UnescapeToml(valueMatch.Groups[2].Value);
                }
            }

            string providerName = GetRequired(global, "model_provider", "Codex 配置缺少 model_provider");
            string model = GetRequired(global, "model", "Codex 配置缺少 model");
            Dictionary<string, string> provider;
            if (!providers.TryGetValue(providerName, out provider))
            {
                throw new InvalidOperationException("找不到当前模型供应商配置：" + providerName);
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> auth = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(authPath));
            object apiKeyValue;
            if (auth == null || !auth.TryGetValue("OPENAI_API_KEY", out apiKeyValue) || apiKeyValue == null ||
                string.IsNullOrWhiteSpace(Convert.ToString(apiKeyValue)))
            {
                throw new InvalidOperationException("Codex 认证文件中没有 OPENAI_API_KEY");
            }

            CcSwitchConfig result = new CcSwitchConfig();
            result.ProviderName = provider.ContainsKey("name") ? provider["name"] : providerName;
            result.Model = model;
            result.BaseUrl = GetRequired(provider, "base_url", "当前供应商配置缺少 base_url");
            result.WireApi = provider.ContainsKey("wire_api") ? provider["wire_api"] : "responses";
            result.ApiKey = Convert.ToString(apiKeyValue);

            Uri endpoint;
            if (!Uri.TryCreate(result.Endpoint, UriKind.Absolute, out endpoint) ||
                (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("当前供应商的 base_url 无效");
            }
            return result;
        }

        private static string GetRequired(Dictionary<string, string> values, string key, string error)
        {
            string value;
            if (!values.TryGetValue(key, out value) || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(error);
            }
            return value;
        }

        private static string UnescapeToml(string value)
        {
            return value.Replace("\\\"", "\"").Replace("\\\\", "\\");
        }
    }
}
