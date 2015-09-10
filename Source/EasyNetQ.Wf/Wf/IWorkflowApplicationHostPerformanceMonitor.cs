using System;

namespace EasyNetQ.Wf
{
    /// <summary>
    /// Interface for monitoring performance of WorkflowApplicationHosts
    /// </summary>
    public interface IWorkflowApplicationHostPerformanceMonitor
    {
        void MessageConsumed(string workflowName);

        void WorkflowFaulted(string workflowName);

        void WorkflowStarted(string workflowName);

        void WorkflowResumed(string workflowName);

        void WorkflowRunning(string workflowName);

        void WorkflowCompleted(string workflowName);

        void WorkflowDuration(string workflowName, TimeSpan duration);
    }

    public class DefaultWorkflowApplicationHostPerformanceMonitor : IWorkflowApplicationHostPerformanceMonitor
    {
        public DefaultWorkflowApplicationHostPerformanceMonitor() { }

        public virtual void MessageConsumed(string workflowName) { }
        
        public virtual void WorkflowFaulted(string workflowName) { }
        
        public virtual void WorkflowStarted(string workflowName) { }        

        public virtual void WorkflowResumed(string workflowName) { }
        
        public virtual void WorkflowRunning(string workflowName) { }
        
        public virtual void WorkflowCompleted(string workflowName) { }   
        
        public virtual void WorkflowDuration(string workflowName, TimeSpan duration) { }
    }
}