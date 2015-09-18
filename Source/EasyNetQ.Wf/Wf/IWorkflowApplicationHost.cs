using System;
using System.Activities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EasyNetQ.Wf
{    
    public interface IWorkflowApplicationHost : IDisposable
    {
        event EventHandler<RequestAdditionalTimeEventArgs> RequestAdditionalTime;

        IWorkflowApplicationHostInstanceStore WorkflowInstanceStore { get; }
        
        Activity WorkflowDefinition { get; }
        
        void Initialize(IDictionary<WorkflowIdentity, Activity> workflowVersionMap);

        bool IsRunning { get; }
        void Start();
        void Stop();

        IEnumerable<ISubscriptionResult> GetSubscriptions();
        void AddSubscription(ISubscriptionResult subscription);
        void CancelSubscription(ISubscriptionResult subscription);

        void OnDispatchMessage(object message);

        Task OnDispatchMessageAsync(object message);        
    }

    public sealed class RequestAdditionalTimeEventArgs : EventArgs
    {
        public TimeSpan Timeout { get; set; }

        public RequestAdditionalTimeEventArgs(TimeSpan timeout)
        {
            Timeout = timeout;
        }
    }

}