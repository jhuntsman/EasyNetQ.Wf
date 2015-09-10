using System;

namespace EasyNetQ.Wf
{
    /// <summary>
    /// Interface for monitoring performance of WorkflowApplicationHosts
    /// </summary>
    public interface IWorkflowApplicationHostPerformanceMonitor
    {
        void MessageConsumed();

        void WorkflowFaulted();

        void WorkflowStarted();

        void WorkflowResumed();

        void WorkflowRunning();

        void WorkflowCompleted();
    }

    public class DefaultWorkflowApplicationHostPerformanceMonitor : IWorkflowApplicationHostPerformanceMonitor
    {
        public DefaultWorkflowApplicationHostPerformanceMonitor() { }

        public virtual void MessageConsumed() { }
        
        public virtual void WorkflowFaulted() { }
        
        public virtual void WorkflowStarted() { }        

        public virtual void WorkflowResumed() { }
        
        public virtual void WorkflowRunning() { }
        
        public virtual void WorkflowCompleted() { }        
    }
}