﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.IO;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.PlatformAbstractions;

namespace CodeComb.vNextExperimentCenter.Node
{
    public class Startup
    {
        public static IConfiguration Configuration;
        private static HttpClient client;
        public static HttpClient Client
        {
            get
            {
                if (client == null)
                {
                    client = new HttpClient();
                    client.BaseAddress = new Uri($"http://{Configuration["Server"]}:{Configuration["Port"]}");
                    client.DefaultRequestHeaders.Add("private-key", Configuration["PrivateKey"]);
                }
                return client;
            }
        }

        public void ConfigureServices(IServiceCollection services)
        {
            var _services = services.BuildServiceProvider();
            var appEnv = _services.GetRequiredService<IApplicationEnvironment>();
            var env = _services.GetRequiredService<IHostingEnvironment>();
            var builder = new ConfigurationBuilder()
                 .AddJsonFile(Path.Combine(appEnv.ApplicationBasePath, $"config.json"))
                 .AddJsonFile(Path.Combine(appEnv.ApplicationBasePath, $"config.testing.json"), optional: true)
                 .AddEnvironmentVariables();
            Configuration = builder.Build();
            services.AddInstance(Configuration);
            services.AddMvc();
            services.AddCIRunner(Convert.ToInt32(Configuration["MaxThread"]));
        }

        private void Task_OnBuildSuccessful(object sender, CI.Runner.EventArgs.BuildSuccessfulArgs args)
        {
            var task = sender as CI.Runner.CITask;
            Client.PostAsync("/api/Judge/Successful", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "id", task.Identifier.ToString() },
                { "Output", task.Output }
            })).Wait();
            cacheStr.Remove(task.Identifier);
            cacheTime.Remove(task.Identifier);
            GC.Collect();
        }

        private void Task_OnTimeLimitExceeded(object sender, CI.Runner.EventArgs.TimeLimitExceededArgs args)
        {
            var task = sender as CI.Runner.CITask;
            Client.PostAsync("/api/Judge/TimeLimitExceeded", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "id", task.Identifier.ToString() },
                { "Output", task.Output }
            })).Wait();
            cacheStr.Remove(task.Identifier);
            cacheTime.Remove(task.Identifier);
            GC.Collect();
        }

        private void Task_OnBuiledFailed(object sender, CI.Runner.EventArgs.BuildFailedArgs args)
        {
            var task = sender as CI.Runner.CITask;
            Client.PostAsync("/api/Judge/Failed", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "id", task.Identifier.ToString() },
                { "Output", task.Output }
            })).Wait();
            cacheStr.Remove(task.Identifier);
            cacheTime.Remove(task.Identifier);
            GC.Collect();
        }

        public Dictionary<string, string> cacheStr = new Dictionary<string, string>();
        public Dictionary<string, DateTime> cacheTime = new Dictionary<string, DateTime>();

        private void Task_OnOutputReceived(object sender, CI.Runner.EventArgs.OutputReceivedEventArgs args)
        {
            var task = sender as CI.Runner.CITask;
            var id = task.Identifier;
            if (!cacheStr.Keys.Contains(id))
                cacheStr[id] = args.Output;
            if (!cacheTime.Keys.Contains(id))
                cacheTime[id] = DateTime.Now;
            cacheStr[id] += args.Output;
            Task.Factory.StartNew(async () =>
            {
                Thread.Sleep(2000);
                if (cacheTime[id].AddSeconds(2) > DateTime.Now)
                {
                    return;
                }
                else
                {
                    cacheTime[id] = DateTime.Now;
                    var tmp = cacheStr[id];
                    cacheStr[id] = "";

                    var result = await Client.PostAsync("/api/Judge/Output", new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        { "id", id.ToString() },
                        { "text", tmp }
                    }));
                    Console.WriteLine($"向服务器反馈输出文本 {result.StatusCode}");
                }
            });
        }

        public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory)
        {
            loggerFactory.MinimumLevel = LogLevel.Warning;
            loggerFactory.AddConsole();

            app.UseMvc();

            CI.Runner.CITask.OnOutputReceived += Task_OnOutputReceived;
            CI.Runner.CITask.OnBuiledFailed += Task_OnBuiledFailed;
            CI.Runner.CITask.OnTimeLimitExceeded += Task_OnTimeLimitExceeded;
            CI.Runner.CITask.OnBuildSuccessful += Task_OnBuildSuccessful;
        }
    }
}
