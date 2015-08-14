using System;
using System.Threading.Tasks;

namespace EasyNetQ.Wf.AutoConsumers
{
    public interface IConsumeAdvanced<in TMessage> where TMessage : class
    {
        void Consume(IMessage<TMessage> messageContext, MessageReceivedInfo messageInfo);
    }
    
    public interface IConsumeAdvancedAsync<in TMessage> where TMessage : class
    {
        Task ConsumeAsync(IMessage<TMessage> messageContext, MessageReceivedInfo messageInfo);
    }     
}