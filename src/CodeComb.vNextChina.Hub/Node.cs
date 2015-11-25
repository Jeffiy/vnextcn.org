using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;
using System.IO;
using Newtonsoft.Json;
using CodeComb.Package;

namespace CodeComb.vNextChina.Hub
{
    public enum NodeStatus
    {
        Free,
        Working,
        Busy,
        Lost
    }

    public class Node
    {
        public string Alias { get; set; }
        public string Server { get; set; }
        public int Port { get; set; }
        public int MaxThread { get; private set; }
        public int CurrentThread { get; private set; }
        public int LostConnectionCount { get; set; } = 0;
        public string PrivateKey { get; set; }
        public OSType OS { get; set; }
        private Timer Timer { get; set; }
        public HttpClient client
        {
            get
            {
                var ret = new HttpClient();
                ret.BaseAddress = new Uri($"http://{Server}:{Port}");
                ret.DefaultRequestHeaders.Accept.Clear();
                ret.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                ret.DefaultRequestHeaders.Add("private-key", PrivateKey);
                return ret;
            }
        }
        public NodeStatus Status
        {
            get
            {
                if (LostConnectionCount > 0)
                    return NodeStatus.Lost;
                else if (CurrentThread == 0)
                    return NodeStatus.Free;
                else if (MaxThread > CurrentThread)
                    return NodeStatus.Working;
                else
                    return NodeStatus.Busy;
            }
        }

        public async Task Abort(string Identifier)
        {
            client.Timeout = new TimeSpan(0, 0, 10);
            await client.PostAsync("/api/ci/Abort", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "id", Identifier }
            }));
        }

        public async Task RefreshNodeInfo()
        {
            client.Timeout = new TimeSpan(0, 0, 10);
            var response = await client.GetAsync("/api/common/GetNodeInfo");
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                Console.Error.WriteLine($"{Alias} 连接失败 " + response.StatusCode);
                return;
            }
            var result = JsonConvert.DeserializeObject<dynamic>(await response.Content.ReadAsStringAsync());
            MaxThread = result.MaxThread;
            CurrentThread = result.CurrentThread;
            OS = (OSType)Enum.Parse(typeof(OSType), result.Platform.ToString());
        }

        public void Init()
        {
            Console.WriteLine($"正在初始化{Alias}");
            Timer = new Timer(x => HeartBeat(), null, 0, 1000 * 10);
            RefreshNodeInfo();
        }

        public async Task HeartBeat()
        {
            client.Timeout = new TimeSpan(0, 0, 10);
            try
            {
                await RefreshNodeInfo();
                LostConnectionCount = 0;
            }
           catch
            {
                LostConnectionCount++;
                Console.Error.WriteLine($"{Alias} 心跳测试失败第{LostConnectionCount}次");
            }
        }

        public async Task<bool> SendCIBuildTask(long id, int RestoreMethod, string Url, string Branch, string Version, string AdditionalEnvironmentVariables)
        {
            var result = await client.PostAsync("/api/ci/newci", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "id", id.ToString() },
                { "Version", Version },
                { "Url", Url.ToString() },
                { "Branch", Branch.ToString() },
                { "RestoreMethod", RestoreMethod.ToString() },
                { "AdditionalEnvironmentVariables", AdditionalEnvironmentVariables }
            }));
            Console.WriteLine($"{Alias} 成功接收任务#{id}");
            return result.StatusCode == System.Net.HttpStatusCode.OK;
        }

        public async Task<bool> SendJudgeTask(long id, byte[] user, byte[] problem, string nuget)
        {
            client.Timeout = new TimeSpan(0, 10, 0);
            using (var content = new MultipartFormDataContent("Upload----" + DateTime.Now.ToString("MM-dd-yyyy HH:mm:ss")))
            {
                content.Add(new StreamContent(new MemoryStream(user)), "user", "user.zip");
                content.Add(new StreamContent(new MemoryStream(problem)), "problem", "problem.zip");
                content.Add(new StringContent(id.ToString()), "id");
                content.Add(new StringContent(nuget), "nuget");
                var result = await client.PostAsync("/api/ci/newjudge", content);
                Console.WriteLine($"{Alias} 成功接收任务#{id}");
                return result.StatusCode == System.Net.HttpStatusCode.OK;
            }
        }
    }
}
