using System;
using System.Activities;
using System.Collections.Generic;

namespace EasyNetQ.Wf
{
    public interface IWorkflowDefinitionRepository
    {        
        IEnumerable<IWorkflowDefinition> Get(string id);
    }

    public interface IWorkflowDefinition
    {
        string Id { get; }

        Activity RootActivity { get; }

#if !NET4
        WorkflowIdentity Identity { get; }
#endif
    }
}