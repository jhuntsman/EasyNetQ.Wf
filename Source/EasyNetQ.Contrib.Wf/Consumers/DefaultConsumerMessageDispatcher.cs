using System;
using System.Threading.Tasks;
using EasyNetQ.AutoSubscribe;

namespace EasyNetQ.Contrib.Consumers
{
    public class DefaultConsumerMessageDispatcher : IConsumerMessageDispatcher
    {        
        protected virtual TConsumer GetConsumer<TConsumer>() 
        {
            TConsumer consumer = (TConsumer)Activator.CreateInstance(typeof(TConsumer));
            return consumer;
        }

        public void Consume<TMessage, TConsumer>(TMessage message)
            where TMessage : class
            where TConsumer : IConsume<TMessage>
        {
            var consumer = (IConsume<TMessage>)GetConsumer<TConsumer>();

            consumer.Consume(message);
        }

        public Task ConsumeAsync<TMessage, TConsumer>(TMessage message)
            where TMessage : class
            where TConsumer : IConsumeAsync<TMessage>
        {
            var consumer = (IConsumeAsync<TMessage>)GetConsumer<TConsumer>();

            return consumer.Consume(message);
        }

        public void ConsumeAdvanced<TMessage, TConsumer>(IMessage<TMessage> message, MessageReceivedInfo info)
            where TMessage : class
            where TConsumer : IConsumeAdvanced<TMessage>
        {
            var consumer = (IConsumeAdvanced<TMessage>)GetConsumer<TConsumer>();

            consumer.Consume(message, info);
        }

        public Task ConsumeAdvancedAsync<TMessage, TConsumer>(IMessage<TMessage> message, MessageReceivedInfo info)
            where TMessage : class
            where TConsumer : IConsumeAdvancedAsync<TMessage>
        {
            var consumer = (IConsumeAdvancedAsync<TMessage>)GetConsumer<TConsumer>();

            return consumer.ConsumeAsync(message, info);
        }

        public TResponse Respond<TRequest, TResponse, TConsumer>(TRequest request)
            where TRequest : class
            where TResponse : class
            where TConsumer: IRespond<TRequest,TResponse>
        {
            var consumer = (IRespond<TRequest,TResponse>)GetConsumer<TConsumer>();

            return consumer.Respond(request);
        }

        public Task<TResponse> RespondAsync<TRequest, TResponse, TConsumer>(TRequest request)
            where TRequest : class
            where TResponse : class
            where TConsumer : IRespondAsync<TRequest, TResponse>
        {
            var consumer = (IRespondAsync<TRequest, TResponse>)GetConsumer<TConsumer>();

            return consumer.Respond(request);
        }        
    }
}