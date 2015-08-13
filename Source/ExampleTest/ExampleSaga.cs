using System;
using System.Activities;
using System.Diagnostics;
using System.IO;
using System.Runtime.DurableInstancing;
using System.Threading;
using EasyNetQ;
using EasyNetQ.AutoSubscribe;
using EasyNetQ.Contrib.Consumers;
using EasyNetQ.Contrib.Wf;


namespace ExampleTest
{
    public class IWorkflowMessage { }
    
    public class ExampleMessage
    {
        public string Name { get; set; }
    }
    
    public class ExampleMessageRequest
    {
        public string Name { get; set; }
    }

    public class ExampleMessageResponse
    {
        public Guid WorkflowId { get; set; }
    }

    public class ChatMessage
    {
        public string Message { get; set; }
    }
    

    public class ExampleConsumer :
        IConsume<ExampleMessage>
    {

        public void Consume(ExampleMessage message)
        {
            throw new NotImplementedException();
        }
    }

    public class AdvancedExampleConsumer : 
        IConsumeAdvanced<ExampleMessageRequest>
    {
        private readonly IBus _bus;
        public AdvancedExampleConsumer(IBus bus)
        {
            _bus = bus;
        }

        public void Consume(IMessage<ExampleMessage> message, MessageReceivedInfo info)
        {
            Console.WriteLine("AdvancedExampleConsumer::Consume<ExampleMessage>");
        }

        public void Consume(IMessage<ExampleMessageRequest> messageContext, MessageReceivedInfo messageInfo)
        {
            Console.WriteLine("AdvancedExampleConsumer::Consume<ExampleMessage>");
                        
            // send back a message
            _bus.Reply(messageContext, new ExampleMessageResponse());
        }
        
    }

    public class ExampleResponder : 
        IConsume<ChatMessage>,        
        IRespond<ExampleMessageRequest, ExampleMessageResponse>
    {
        public ExampleResponder(IEasyNetQLogger logger)
        {
            
        }

        public ExampleMessageResponse Respond(ExampleMessageRequest request)
        {
            throw new NotImplementedException();
        }

        public void Consume(ChatMessage message)
        {
            throw new NotImplementedException();
        }
    }
}