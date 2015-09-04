using System;
using System.Activities;
using System.ComponentModel;

namespace EasyNetQ.Wf.Activities
{
    public sealed class BusPublish<TMessage> : CodeActivity, IPublishMessageActivity
        where TMessage:class
    {
        [RequiredArgument]
        public InArgument<TMessage> Message { get; set; }

        [RequiredArgument]
        public InArgument<string> WorkflowRouteTopic { get; set; }

        [DefaultValue(null)]
        public InArgument<string> Topic { get; set; }
                            
        protected override void Execute(CodeActivityContext context)
        {                        
            TMessage messageBody = this.Message.Get(context);
            string topic = this.Topic.Get(context);
            string workflowRouteTopic = this.WorkflowRouteTopic.Get(context);

            /*
            // old publish method
            IBus bus = context.GetExtension<IBus>();
            var workflowStrategy = bus.Advanced.Container.Resolve<IWorkflowConsumerHostStrategies>();
            var message = workflowStrategy.CreateWorkflowMessage(context, messageBody, workflowRouteTopic);            
            workflowStrategy.PublishAdvanced(message, topic);
            */

            // publish using the WorkflowApplicationHost
            var publishService = context.GetExtension<IWorkflowApplicationHostBehavior>();
            publishService.PublishMessageWithCorrelation(context.WorkflowInstanceId, messageBody, topic);
        }
    }
}