using System;
using System.Activities;
using System.Collections.Generic;
using System.IO;

namespace EasyNetQ.Wf
{
    public interface IWorkflowDefinitionRepository
    {        
        IEnumerable<IWorkflowDefinition> Get(WorkflowIdentity identity);        
    }

    public interface IWorkflowDefinition
    {                        
        string Name { get; }

        Activity RootActivity { get; }

        WorkflowIdentity Identity { get; }

        string Hosts { get; }
    }

#if NET4    
    // Shim class definition for System.Activities.WorkflowIdentity
    // This does not mean that side-by-side versioning will be available in WF4
    public class WorkflowIdentity
    {
        public string Name {get;set;}
        public string Package {get;set;}
        public Version Version {get;set;}
    }
#endif
}