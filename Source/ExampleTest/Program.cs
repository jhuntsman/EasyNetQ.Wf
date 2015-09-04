using System;
using System.Activities.DurableInstancing;
using System.Runtime.DurableInstancing;
using EasyNetQ;
using EasyNetQ.DI;
using EasyNetQ.Wf;
using EasyNetQ.Wf.AutoConsumers;
using Ninject;

namespace ExampleTest
{
    class Program
    {               
        private static void Main(string[] args)
        {
            IKernel kernel = new StandardKernel();
            
            kernel.Bind<InstanceStore>()
                .ToMethod(
                    ctx =>
                        new SqlWorkflowInstanceStore("Server=.\\SQL2012;Initial Catalog=WorkflowInstances;Integrated Security=SSPI")
                        {
                            InstanceCompletionAction = InstanceCompletionAction.DeleteAll,
                            InstanceLockedExceptionAction = InstanceLockedExceptionAction.AggressiveRetry                            
                        })
                .InSingletonScope();                        

            RabbitHutch.SetContainerFactory(()=>new NinjectAdapter(kernel));
            using (var bus = RabbitHutch.CreateBus("host=localhost;virtualHost=/;username=test;password=test", (s)=> s.UseWorkflowOrchestration()))
            {   
                    
                bus.SubscribeForOrchestration<ExampleWorkflow>("wf-ExampleWorkflow");
                
                bus.SubscribeConsumer<AdvancedExampleConsumer>("wf-autosubscriber-example");
                
                Console.WriteLine("Bus listening...");

                Console.WriteLine();
                Console.Write("Enter your name: ");
                string name = Console.ReadLine();

                bus.Publish(new ExampleMessage() { Name = name}, "ExampleWorkflow");
                
                Console.ReadLine();
            }
        }
    }    
}
