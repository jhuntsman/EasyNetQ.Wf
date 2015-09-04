using System;
using System.Activities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace EasyNetQ.Wf
{    
    public interface IWorkflowApplicationHost : IDisposable
    {
        IWorkflowApplicationHostInstanceStore WorkflowInstanceStore { get; }

        void Initialize(Activity workflowDefinition);

        bool IsRunning { get; }
        void Start();
        void Stop();

        IEnumerable<ISubscriptionResult> GetSubscriptions();
        void AddSubscription(ISubscriptionResult subscription);
        void CancelSubscription(ISubscriptionResult subscription);

        void OnDispatchMessage(object message);

        Task OnDispatchMessageAsync(object message);        
    }
}