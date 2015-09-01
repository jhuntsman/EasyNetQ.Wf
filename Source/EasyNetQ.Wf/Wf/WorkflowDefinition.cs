using System;
using System.Activities;

namespace EasyNetQ.Wf
{
    public interface IWorkflowDefinition
    {
        Activity RootActivity { get; }

        string Name { get; }

        string Version { get; }

        string PackageNamespace { get; }
    }

    public class WorkflowDefinition : IWorkflowDefinition
    {
        public Activity RootActivity { get; private set; }

        public string Name { get; private set; }

        public string Version { get; private set; }

        public string PackageNamespace { get; private set; }
        
        public WorkflowDefinition(Activity rootActivity)
        {
            RootActivity = rootActivity;
            Name = rootActivity.GetType().FullName;
            Version = "1";
            PackageNamespace = "http://tempuri.org/EasyNetQ-Wf";
        }        
    }
}