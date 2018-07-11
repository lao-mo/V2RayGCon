﻿using Ionic.Zip;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using static V2RayGCon.Lib.StringResource;

namespace V2RayGCon.Lib
{
    public class Utils
    {
        #region Json
        static JObject ExtractJObjectPart(string key, JObject source)
        {
            var result = JObject.Parse("{}");
            var part = Lib.Utils.GetKey(source, key);
            if (part != null)
            {
                result[key] = part.DeepClone();
            }
            return result;
        }

        public static void RemoveKeyFromJson(JObject json, string path)
        {
            var index = path.LastIndexOf('.');
            JObject parent;
            string key;
            if (index < 0)
            {
                key = path;
                parent = json;
            }
            else
            {
                parent = Lib.Utils.GetKey(
                    json, path.Substring(0,
                    Math.Min(path.Length, index)))
                    as JObject;

                key = path.Substring(Math.Min(path.Length - 1, index + 1));
            }

            if (parent == null)
            {
                throw new JsonReaderException();
            }

            (parent as JObject).Property(key)?.Remove();
        }

        public static JObject MergeConfig(JObject left, JObject right)
        {
            var l = left.DeepClone() as JObject;
            var r = right.DeepClone() as JObject;

            var result = JObject.Parse("{}");
            foreach (var part in new JObject[] { r, l })
            {
                foreach (var key in new string[] { "inboundDetour", "outboundDetour" })
                {
                    var mergeOptionForDtr = new JsonMergeSettings
                    {
                        MergeArrayHandling = MergeArrayHandling.Concat,
                        MergeNullValueHandling = MergeNullValueHandling.Ignore
                    };

                    result.Merge(ExtractJObjectPart(key, part), mergeOptionForDtr);
                    RemoveKeyFromJson(part, key);
                }
            }

            result = MergeJson(result, l);
            return MergeJson(result, r);
        }

        public static JObject LoadExamples()
        {
            return JObject.Parse(resData("config_def"));
        }

        public static JObject MergeJson(JObject firstJson, JObject secondJson)
        {
            var result = firstJson.DeepClone() as JObject; // copy
            result.Merge(secondJson, new JsonMergeSettings
            {
                MergeArrayHandling = MergeArrayHandling.Merge,
                MergeNullValueHandling = MergeNullValueHandling.Merge
            });

            return result;
        }

        public static T Parse<T>(string json) where T : JToken
        {
            if (json == string.Empty)
            {
                return null;
            }
            return (T)JToken.Parse(json);
        }

        public static JToken GetKey(JToken json, string path)
        {
            var curPos = json;
            var keys = path.Split('.');

            int depth;
            for (depth = 0; depth < keys.Length; depth++)
            {
                if (curPos == null || !curPos.HasValues)
                {
                    break;
                }

                if (int.TryParse(keys[depth], out int n))
                {
                    curPos = curPos[n];
                }
                else
                {
                    curPos = curPos[keys[depth]];
                }
            }

            return depth < keys.Length ? null : curPos;
        }

        public static T GetValue<T>(JToken json, string prefix, string key)
        {
            return GetValue<T>(json, $"{prefix}.{key}");
        }

        public static T GetValue<T>(JToken json, string keyChain)
        {
            var key = GetKey(json, keyChain);

            var def = default(T) == null && typeof(T) == typeof(string) ?
                (T)(object)string.Empty :
                default(T);

            if (key == null)
            {
                return def;
            }
            try
            {
                return key.Value<T>();
            }
            catch { }
            return def;
        }

        public static Func<string, string, string> GetStringByPrefixAndKeyHelper(JObject json)
        {
            var o = json;
            return (prefix, key) =>
            {
                return GetValue<string>(o, $"{prefix}.{key}");
            };
        }

        public static Func<string, string> GetStringByKeyHelper(JObject json)
        {
            var o = json;
            return (key) =>
            {
                return GetValue<string>(o, $"{key}");
            };
        }

        public static string GetAddr(JObject json, string prefix, string keyIP, string keyPort)
        {
            var ip = GetValue<String>(json, prefix, keyIP) ?? "127.0.0.1";
            var port = GetValue<string>(json, prefix, keyPort);
            return string.Join(":", ip, port);
        }

        #endregion

        #region convert

        public static string Config2Base64String(JObject config)
        {
            return Base64Encode(config.ToString(Formatting.None));
        }

        public static List<string> Str2ListStr(string serial)
        {
            var list = new List<string> { };
            var items = serial.Split(',');
            foreach (var item in items)
            {
                if (!string.IsNullOrEmpty(item))
                {
                    list.Add(item);
                }

            }
            return list;
        }

        public static List<string> ExtractLinks(string text, Model.Data.Enum.LinkTypes linkType)
        {
            string pattern = GenPattern(linkType);
            var matches = Regex.Matches("\n" + text, pattern, RegexOptions.IgnoreCase);
            var links = new List<string>();
            foreach (Match match in matches)
            {
                links.Add(match.Value.Substring(1));
            }
            return links;
        }

        public static string Vmess2VmessLink(Model.Data.Vmess vmess)
        {
            if (vmess == null)
            {
                return string.Empty;
            }

            string content = JsonConvert.SerializeObject(vmess);
            return AddLinkPrefix(
                Base64Encode(content),
                Model.Data.Enum.LinkTypes.vmess);
        }

        public static Model.Data.Vmess VmessLink2Vmess(string link)
        {
            try
            {
                string plainText = Base64Decode(GetLinkBody(link));
                var vmess = JsonConvert.DeserializeObject<Model.Data.Vmess>(plainText);
                if (!string.IsNullOrEmpty(vmess.add)
                    && !string.IsNullOrEmpty(vmess.port)
                    && !string.IsNullOrEmpty(vmess.aid))
                {

                    return vmess;
                }
            }
            catch { }
            return null;
        }

        public static Model.Data.Shadowsocks SSLink2SS(string ssLink)
        {
            string b64 = GetLinkBody(ssLink);

            try
            {
                var ss = new Model.Data.Shadowsocks();
                var plainText = Base64Decode(b64);
                var parts = plainText.Split('@');
                var mp = parts[0].Split(':');
                if (parts[1].Length > 0 && mp[0].Length > 0 && mp[1].Length > 0)
                {
                    ss.method = mp[0];
                    ss.pass = mp[1];
                    ss.addr = parts[1];
                }
                return ss;
            }
            catch { }
            return null;
        }

        public static JObject SSLink2Config(string ssLink)
        {
            Model.Data.Shadowsocks ss = SSLink2SS(ssLink);
            if (ss == null)
            {
                return null;
            }

            TryParseIPAddr(ss.addr, out string ip, out int port);
            var tpl = JObject.Parse(resData("config_tpl"));
            var config = tpl["tplImportSS"];

            var setting = config["outbound"]["settings"]["servers"][0];
            setting["address"] = ip;
            setting["port"] = port;
            setting["method"] = ss.method;
            setting["password"] = ss.pass;

            return config.DeepClone() as JObject;
        }

        public static Model.Data.Vmess ConfigString2Vmess(string config)
        {
            JObject json;
            try
            {
                json = JObject.Parse(config);
            }
            catch
            {
                return null;
            }

            var GetStr = GetStringByPrefixAndKeyHelper(json);

            Model.Data.Vmess vmess = new Model.Data.Vmess();
            vmess.v = "2";
            vmess.ps = GetStr("v2raygcon", "alias");

            var prefix = "outbound.settings.vnext.0";
            vmess.add = GetStr(prefix, "address");
            vmess.port = GetStr(prefix, "port");
            vmess.id = GetStr(prefix, "users.0.id");
            vmess.aid = GetStr(prefix, "users.0.alterId");

            prefix = "outbound.streamSettings";
            vmess.net = GetStr(prefix, "network");
            vmess.type = GetStr(prefix, "kcpSettings.header.type");
            vmess.tls = GetStr(prefix, "security");

            switch (vmess.net)
            {
                case "ws":
                    vmess.path = GetStr(prefix, "wsSettings.path");
                    vmess.host = GetStr(prefix, "wsSettings.headers.Host");
                    break;
                case "h2":
                    try
                    {
                        vmess.path = GetStr(prefix, "httpSettings.path");
                        var hosts = json["outbound"]["streamSettings"]["httpSettings"]["host"];
                        vmess.host = JArray2Str(hosts as JArray);
                    }
                    catch { }
                    break;
            }


            return vmess;
        }

        public static JObject Vmess2Config(Model.Data.Vmess vmess)
        {
            if (vmess == null)
            {
                return null;
            }

            // prepare template
            var tpl = JObject.Parse(resData("config_tpl"));
            var config = tpl["tplImportVmess"];
            config["v2raygcon"]["alias"] = vmess.ps;

            var cPos = config["outbound"]["settings"]["vnext"][0];
            cPos["address"] = vmess.add;
            cPos["port"] = Lib.Utils.Str2Int(vmess.port);
            cPos["users"][0]["id"] = vmess.id;
            cPos["users"][0]["alterId"] = Lib.Utils.Str2Int(vmess.aid);

            // insert stream type
            string[] streamTypes = { "ws", "tcp", "kcp", "h2" };
            string streamType = vmess.net.ToLower();

            if (!streamTypes.Contains(streamType))
            {
                return config.DeepClone() as JObject;
            }

            config["outbound"]["streamSettings"] = tpl[streamType];

            try
            {
                switch (streamType)
                {
                    case "kcp":
                        config["outbound"]["streamSettings"]["kcpSettings"]["header"]["type"] = vmess.type;
                        break;
                    case "ws":
                        config["outbound"]["streamSettings"]["wsSettings"]["path"] =
                            string.IsNullOrEmpty(vmess.v) ? vmess.host : vmess.path;
                        if (vmess.v == "2" && !string.IsNullOrEmpty(vmess.host))
                        {
                            config["outbound"]["streamSettings"]["wsSettings"]["headers"]["Host"] = vmess.host;
                        }
                        break;
                    case "h2":
                        config["outbound"]["streamSettings"]["httpSettings"]["path"] = vmess.path;
                        config["outbound"]["streamSettings"]["httpSettings"]["host"] = Str2JArray(vmess.host);
                        break;
                }

            }
            catch { }

            try
            {
                // must place at the end. cos this key is add by streamSettings
                config["outbound"]["streamSettings"]["security"] = vmess.tls;
            }
            catch { }
            return config.DeepClone() as JObject;
        }

        public static JArray Str2JArray(string content)
        {
            var arr = new JArray();
            var items = content.Replace(" ", "").Split(',');
            foreach (var item in items)
            {
                if (item.Length > 0)
                {
                    arr.Add(item);
                }
            }
            return arr;
        }

        public static string JArray2Str(JArray array)
        {
            if (array == null)
            {
                return string.Empty;
            }
            List<string> s = new List<string>();

            foreach (var item in array.Children())
            {
                try
                {
                    var v = item.Value<string>();
                    if (!string.IsNullOrEmpty(v))
                    {
                        s.Add(v);
                    }
                }
                catch { }
            }

            if (s.Count <= 0)
            {
                return string.Empty;
            }
            return string.Join(",", s);
        }

        public static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }

        static string Base64PadRight(string base64)
        {
            return base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        }

        public static string Base64Decode(string base64EncodedData)
        {
            if (string.IsNullOrEmpty(base64EncodedData))
            {
                return string.Empty;
            }
            var padded = Base64PadRight(base64EncodedData);
            var base64EncodedBytes = System.Convert.FromBase64String(padded);
            return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
        }

        #endregion

        #region net

        static string FetchFromCache(string url)
        {
            var cache = Service.Cache.Instance.
                GetCache<string>(resData("CacheHTML")).
                Item2;

            if (cache.ContainsKey(url))
            {
                return cache[url];
            }
            return null;
        }

        static void UpdateHTMLCache(string url, string html)
        {
            if (html == null || string.IsNullOrEmpty(html))
            {
                return;
            }

            var cache = Service.Cache.Instance.
                GetCache<string>(resData("CacheHTML"));

            lock (cache.Item1)
            {
                cache.Item2[url] = html;
            }
        }

        public static string Fetch(string url, int timeout = -1, bool useCache = false)
        {
            if (useCache)
            {
                var cache = FetchFromCache(url);
                if (cache != null)
                {
                    return cache;
                }
            }

            var html = string.Empty;

            Lib.Utils.SupportProtocolTLS12();
            using (WebClient wc = new TimedWebClient
            {
                Encoding = System.Text.Encoding.UTF8,
                Timeout = timeout,
            })
            {
                /* 如果用抛出异常的写法
                 * task中调用此函数时
                 * 会弹出用户未处理异常警告
                 */
                try
                {
                    html = wc.DownloadString(url);
                    UpdateHTMLCache(url, html);
                }
                catch { }
            }
            return html;
        }

        public static string GetLatestVGCVersion()
        {
            string html = Fetch(resData("UrlLatestVGC"), 10000);

            if (string.IsNullOrEmpty(html))
            {
                return string.Empty;
            }

            string p = resData("PatternLatestVGC");
            var match = Regex.Match(html, p, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return string.Empty;
        }

        public static List<string> GetCoreVersions()
        {
            List<string> versions = new List<string> { };

            string html = Fetch(resData("ReleasePageUrl"), 10000);

            if (string.IsNullOrEmpty(html))
            {
                return versions;
            }

            string pattern = resData("PatternDownloadLink");
            var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                var v = match.Groups[1].Value;
                versions.Add(v);
            }

            return versions;
        }
        #endregion

        #region Miscellaneous

        private static Random random = new Random();
        public static string RandomHex(int length)
        {
            //  https://stackoverflow.com/questions/1344221/how-can-i-generate-random-alphanumeric-strings-in-c
            if (length <= 0)
            {
                return string.Empty;
            }

            const string chars = "0123456789abcdef";
            return new string(Enumerable.Repeat(chars, length)
              .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        public static int Clamp(int value, int min, int max)
        {
            return Math.Max(Math.Min(value, max - 1), min);
        }

        public static int GetIndexIgnoreCase(Dictionary<int, string> dict, string value)
        {
            foreach (var data in dict)
            {
                if (!string.IsNullOrEmpty(data.Value)
                    && data.Value.Equals(value, StringComparison.CurrentCultureIgnoreCase))
                {
                    return data.Key;
                }
            }
            return -1;
        }

        public static string CutStr(string s, int len)
        {
            if (len >= s.Length || len < 1)
            {
                return s;
            }

            return s.Substring(0, len) + " ...";
        }

        public static int Str2Int(string value)
        {
            if (float.TryParse(value, out float f))
            {
                return (int)Math.Round(f);
            };
            return 0;
        }

        public static bool TryParseIPAddr(string address, out string ip, out int port)
        {
            ip = "127.0.0.1";
            port = 1080;

            string[] parts = address.Split(':');
            if (parts.Length != 2)
            {
                return false;
            }

            ip = parts[0];
            port = Clamp(Str2Int(parts[1]), 0, 65536);
            return true;
        }

        static string GetLinkPrefix(Model.Data.Enum.LinkTypes linkType)
        {
            return Model.Data.Table.linkPrefix[(int)linkType];
        }

        public static string GenPattern(Model.Data.Enum.LinkTypes linkType)
        {
            return string.Format(
               "{0}{1}{2}",
               resData("PatternNonAlphabet"), // vme[ss]
               GetLinkPrefix(linkType),
               resData("PatternBase64"));
        }

        public static string AddLinkPrefix(string b64Content, Model.Data.Enum.LinkTypes linkType)
        {
            return GetLinkPrefix(linkType) + b64Content;
        }

        public static string GetLinkBody(string link)
        {
            Regex re = new Regex("[a-zA-Z0-9]+://");
            return re.Replace(link, string.Empty);
        }

        public static void ZipFileDecompress(string fileName)
        {
            // let downloader handle exception
            using (ZipFile zip = ZipFile.Read(fileName))
            {
                var flattenFoldersOnExtract = zip.FlattenFoldersOnExtract;
                zip.FlattenFoldersOnExtract = true;
                zip.ExtractAll(GetAppDir(), ExtractExistingFileAction.OverwriteSilently);
                zip.FlattenFoldersOnExtract = flattenFoldersOnExtract;
            }
        }
        #endregion

        #region UI related
        public static void CopyToClipboardAndPrompt(string content)
        {
            MessageBox.Show(
                Lib.Utils.CopyToClipboard(content) ?
                I18N("CopySuccess") :
                I18N("CopyFail"));
        }

        public static bool CopyToClipboard(string content)
        {
            try
            {
                Clipboard.SetText(content);
                return true;
            }
            catch { }
            return false;
        }

        public static string GetAppDir()
        {
            return Path.GetDirectoryName(Application.ExecutablePath);
        }

        public static void SupportProtocolTLS12()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
        }

        public static string GetClipboardText()
        {
            if (Clipboard.ContainsText(TextDataFormat.Text))
            {
                return Clipboard.GetText(TextDataFormat.Text);

            }
            return string.Empty;
        }
        #endregion

        #region process

        public static List<TResult> ExecuteInParallel<TParam, TResult>(List<TParam> values, Func<TParam, TResult> lamda)
        {
            var result = new List<TResult>();

            if (values.Count <= 0)
            {
                return result;
            }

            var taskList = new List<Task<TResult>>();
            foreach (var value in values)
            {
                var task = new Task<TResult>(() => lamda(value));
                taskList.Add(task);
                task.Start();
            }
            try
            {
                Task.WaitAll(taskList.ToArray());
            }
            catch (AggregateException ae)
            {
                foreach (var e in ae.InnerExceptions)
                {
                    throw e;
                }
            }

            foreach (var task in taskList)
            {
                result.Add(task.Result);
            }

            return result;
        }

        public static void RunAsSTAThread(Action lamda)
        {
            // https://www.codeproject.com/Questions/727531/ThreadStateException-cant-handeled-in-ClipBoard-Se
            AutoResetEvent @event = new AutoResetEvent(false);
            Thread thread = new Thread(
                () =>
                {
                    lamda();
                    @event.Set();
                });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            @event.WaitOne();
        }


        public static void KillProcessAndChildrens(int pid)
        {
            ManagementObjectSearcher processSearcher = new ManagementObjectSearcher
              ("Select * From Win32_Process Where ParentProcessID=" + pid);
            ManagementObjectCollection processCollection = processSearcher.Get();

            // We must kill child processes first!
            if (processCollection != null)
            {
                foreach (ManagementObject mo in processCollection)
                {
                    KillProcessAndChildrens(Convert.ToInt32(mo["ProcessID"])); //kill child processes(also kills childrens of childrens etc.)
                }
            }

            // Then kill parents.
            try
            {
                Process proc = Process.GetProcessById(pid);
                if (!proc.HasExited)
                {
                    proc.Kill();
                    proc.WaitForExit(1000);
                }
            }
            catch
            {
                // Process already exited.
            }
        }
        #endregion
    }
}
