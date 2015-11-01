﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CodeComb.CI.Runner
{
    public class CIRunner
    {
        public CIRunner(int MaxThreads = 4, int MaxTimeLimit = 1000 * 60 * 20)
        {
            this.MaxThreads = MaxThreads;
            this.MaxTimeLimit = MaxTimeLimit;

            CITask.OnBuildSuccessful += (sender, e) =>
            {
                CurrentTasks.Remove(sender as CITask);
            };

            CITask.OnBuiledFailed += (sender, e) =>
            {
                CurrentTasks.Remove(sender as CITask);
            };

            CITask.OnTimeLimitExceeded += (sender, e) =>
            {
                CurrentTasks.Remove(sender as CITask);
            };

            this.Timer = new Timer((x) => 
            {
                if (WaitingTasks.Count == 0 || CurrentTasks.Count >= MaxThreads) return;
                CurrentTasks.Add(WaitingTasks.Dequeue());
                foreach(var z in CurrentTasks.Where(y => y.Status == CITaskStatus.Queued))
                    Task.Factory.StartNew(() =>
                    {
                        z.Run();
                    });
            }, null, 0, 5000);
        }

        public bool IsInQueue(string identifier)
        {
            return WaitingTasks.Any(x => x.Identifier == identifier);
        }

        public bool IsInBuilding(string identifier)
        {
            return CurrentTasks.Any(x => x.Identifier == identifier);
        }

        public bool IsInThisNode(string identifier)
        {
            return IsInQueue(identifier) || IsInBuilding(identifier);
        }

        private Timer Timer { get; set; }
        public int MaxThreads { get; private set; }
        public int MaxTimeLimit { get; private set; }
        public List<CITask> CurrentTasks { get; set; } = new List<CITask>();
        public Queue<CITask> WaitingTasks { get; set; } = new Queue<CITask>();
    }
}
