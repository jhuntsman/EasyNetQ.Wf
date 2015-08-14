
using System;

namespace EasyNetQ.Wf
{
    internal interface IConsumerHostMessageDispatcher<TMessage> where TMessage:class
    {
        //void InitialConsumeAdvanced<TWorkflow, TMessage>(IMessage<TMessage> message, MessageReceivedInfo info)
        //    where TWorkflow : Activity, new();

        void ConsumeAdvanced(IMessage<TMessage> message, MessageReceivedInfo info);
    }
}