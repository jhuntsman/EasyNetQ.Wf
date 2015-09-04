using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using EasyNetQ.Wf.DurableInstancing.Data;

namespace EasyNetQ.Wf.DurableInstancing
{
    public class WorkflowInstanceCommands
    {
        private readonly IEasyNetQLogger Log;
        private readonly IWorkflowDurableInstancingUnitOfWork DbContext;

        public WorkflowInstanceCommands(IEasyNetQLogger logger, IWorkflowDurableInstancingUnitOfWork dataContext)
        {
            Log = logger;
            DbContext = dataContext;
        }
    }
}
