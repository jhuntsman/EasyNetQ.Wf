using System;
using System.Activities;
using System.Collections.Generic;

namespace EasyNetQ.Wf
{
    public interface IWorkflowDefinitionRepository
    {
        
    }

    public interface IWorkflowDefinition
    {
        Activity RootActivity { get; }

        string Name { get; }

        string Package { get; }

        Version Version { get; }        

        string SubscriptionId { get; }
    }

    public class WorkflowDefinition : IWorkflowDefinition
    {
        public string SubscriptionId { get { return RootActivity.GetType().FullName; } }

        public Activity RootActivity { get; private set; }

        public string Name { get { return RootActivity.GetType().Name; } }

        public Version Version { get; set; }

        public string Package { get { return RootActivity.GetType().Namespace; } }
        
        public WorkflowDefinition(Activity rootActivity)
        {            
            RootActivity = rootActivity;                        
            Version = new Version(1, 0);            
        }

        public override string ToString()
        {
            return String.Format("{0}.{1},{2}", Package, Name, Version.ToString(2));
        }
    }
}