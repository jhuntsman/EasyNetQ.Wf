using System;

namespace EasyNetQ.Wf
{
    public interface IRunnableConsumerHost : IDisposable
    {
        bool IsRunning { get; }
        void Start();
        void Stop();
    }
}