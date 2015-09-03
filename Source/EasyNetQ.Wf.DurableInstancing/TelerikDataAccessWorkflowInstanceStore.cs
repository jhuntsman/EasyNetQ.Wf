using System;
using System.Activities.DurableInstancing;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.DurableInstancing;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using EasyNetQ.Wf.DurableInstancing.Data;
using Telerik.OpenAccess;

namespace EasyNetQ.Wf.DurableInstancing
{    
    public class TelerikDataAccessWorkflowInstanceStore : InstanceStore
    {
        private static readonly object _syncLock = new object();

        private readonly WorkflowInstanceCommands Commands;
        private readonly IEasyNetQLogger Log;
        
        public TelerikDataAccessWorkflowInstanceStore(IEasyNetQLogger logger, WorkflowInstanceCommands commands) : base()
        {
            Log = logger;
            Commands = commands;
        }
        
        protected override void OnFreeInstanceHandle(InstanceHandle instanceHandle, object userContext)
        {
            base.OnFreeInstanceHandle(instanceHandle, userContext);
        }

        protected override object OnNewInstanceHandle(InstanceHandle instanceHandle)
        {
            return base.OnNewInstanceHandle(instanceHandle);
        }

        protected override IAsyncResult BeginTryCommand(InstancePersistenceContext context, InstancePersistenceCommand command, TimeSpan timeout, AsyncCallback callback, object state)
        {
            if(context == null) throw new ArgumentNullException("context");
            if(command == null) throw new ArgumentNullException("command");

            // Log which commands we are receiving
            Debug.WriteLine("InstanceStore::BeginTryCommand::{0} received", command.GetType().Name);

            IAsyncResult result = null;  
            
            // validate the store lock
                      
            if (command is CreateWorkflowOwnerCommand)
            {
                result = CreateWorkflowOwner(context, (CreateWorkflowOwnerCommand) command, timeout, callback, state);
            }
                        
            if (result == null)
            {
                // Log which commands we are not handling
                Debug.WriteLine("InstanceStore::BeginTryCommand::{0} was not implemented", command.GetType().Name);

                // The base.BeginTryCommand will return a false (unhandled) return value
                return base.BeginTryCommand(context, command, timeout, callback, state);
            }
            return result;
        }

        private IAsyncResult CreateWorkflowOwner(InstancePersistenceContext context, CreateWorkflowOwnerCommand command, TimeSpan timeout, AsyncCallback callback, object state)
        {            
            var owner = new LockOwner()
            {
                Id = Guid.NewGuid(),                
            };

            // TODO: map fields into the owner entity
            Debug.WriteLine("CreateWorkflowOwner::InstanceOwnerMetadata: ");
            Debug.Indent();
            foreach (var key in command.InstanceOwnerMetadata.Keys)
            {
                Debug.WriteLine("[{0}]=[{1}]", key, command.InstanceOwnerMetadata[key].Value);
            }            
            Debug.Unindent();
            


            //context.BindInstanceOwner(owner.Id, owner.Id);
            //context.BindEvent(HasRunnableWorkflowEvent.Value);

            //return new CompletedAsyncResult(callback, state);

            return null;
        }

        protected override bool EndTryCommand(IAsyncResult result)
        {
            var handledCommand = result as CompletedAsyncResult;
            if (handledCommand == null)
                return base.EndTryCommand(result);

            // complete the handled command
            CompletedAsyncResult.End(result);                            
            return true;
        }
    }
}
