using System;
using System.Threading.Tasks;
using EasyNetQ.AutoSubscribe;


namespace EasyNetQ.Contrib.Consumers
{
    public interface IConsumerMessageDispatcher
    {
        void Consume<TMessage, TConsumer>(TMessage message)
            where TMessage : class
            where TConsumer : IConsume<TMessage>;

        Task ConsumeAsync<TMessage, TConsumer>(TMessage message)
            where TMessage : class
            where TConsumer : IConsumeAsync<TMessage>;

        void ConsumeAdvanced<TMessage, TConsumer>(IMessage<TMessage> message, MessageReceivedInfo info)
            where TMessage : class
            where TConsumer : IConsumeAdvanced<TMessage>;

        Task ConsumeAdvancedAsync<TMessage, TConsumer>(IMessage<TMessage> message, MessageReceivedInfo info)
            where TMessage : class
            where TConsumer : IConsumeAdvancedAsync<TMessage>;

        TResponse Respond<TRequest, TResponse, TConsumer>(TRequest request)
            where TRequest : class
            where TResponse : class
            where TConsumer : IRespond<TRequest, TResponse>;

        Task<TResponse> RespondAsync<TRequest, TResponse, TConsumer>(TRequest request)
            where TRequest : class
            where TResponse : class
            where TConsumer : IRespondAsync<TRequest, TResponse>;        
    }
}