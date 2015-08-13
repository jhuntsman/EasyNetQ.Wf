using System;
using System.Activities;

namespace EasyNetQ.Contrib.Wf.Activities
{    
    public sealed class ReplyMessage<TSource, TReply> : CodeActivity, IPublishMessageActivity
        where TSource: IMessage<TSource> 
        where TReply : class

    {
        [RequiredArgument]
        public InArgument<TReply> Reply { get; set; }

        [RequiredArgument]
        public InArgument<IMessage<TSource>> Source { get; set; }
                
        protected override void Execute(CodeActivityContext context)
        {
            var bus = context.GetExtension<IBus>();
            var workflowStrategy = bus.Advanced.Container.Resolve<IWorkflowConsumerHostStrategies>();

            IMessage<TSource> sourceMessage = this.Source.Get(context);
            TReply replyMessageBody = this.Reply.Get(context);
                        
            workflowStrategy.Reply(sourceMessage, replyMessageBody);
        }
    }
}