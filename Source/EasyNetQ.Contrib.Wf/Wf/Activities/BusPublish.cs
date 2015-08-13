using System;
using System.Activities;
using System.ComponentModel;
using EasyNetQ.Contrib.Consumers;

namespace EasyNetQ.Contrib.Wf.Activities
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
            IBus bus = context.GetExtension<IBus>();
            var workflowStrategy = bus.Advanced.Container.Resolve<IWorkflowConsumerHostStrategies>();

            TMessage messageBody = this.Message.Get(context);
            string workflowRouteTopic = this.WorkflowRouteTopic.Get(context);

            var message = workflowStrategy.CreateWorkflowMessage(context, messageBody, workflowRouteTopic);

            string topic = this.Topic.Get(context);
            workflowStrategy.PublishAdvanced(message, topic);            
        }
    }
}