using System;

namespace EasyNetQ.Wf
{
    internal class DefaultConsumerHostMessageDispatcher<TMessage> : IConsumerHostMessageDispatcher<TMessage> where TMessage:class
    {        
        private readonly ConsumerHost Host;

        public DefaultConsumerHostMessageDispatcher(ConsumerHost host)
        {                        
            Host = host;
        }

        /*
        public void OnInitiate<TWorkflow, TMessage>(IMessage<TMessage> message, MessageReceivedInfo info) where TWorkflow : System.Activities.Activity, new()
        {
            throw new System.NotImplementedException();
        }

        public void OnConsume<TWorkflow, TMessage>(IMessage<TMessage> message, MessageReceivedInfo info) where TWorkflow : System.Activities.Activity, new()
        {
            throw new System.NotImplementedException();
        }
        */

        public void ConsumeAdvanced(IMessage<TMessage> message, MessageReceivedInfo info) 
        {
            Host.OnConsumeAdvanced(message, info);
        }
    }
}