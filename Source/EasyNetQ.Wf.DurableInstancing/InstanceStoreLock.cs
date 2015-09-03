using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EasyNetQ.Wf.DurableInstancing
{
    class InstanceStoreLock
    {
        private object _syncLock = new object();
        
        // Tasks to Handle while Lock is Valid
        private Task LockRenewalTask { get; set; }
        

    }
}
