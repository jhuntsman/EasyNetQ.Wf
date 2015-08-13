using System;
using System.Activities.DurableInstancing;
using System.Runtime.DurableInstancing;
using EasyNetQ;
using EasyNetQ.Contrib.Consumers;
using EasyNetQ.DI;
using EasyNetQ.Contrib.Wf;
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
            //kernel.RegisterAsEasyNetQContainerFactory();
                        
            //using (var bus = RabbitHutch.CreateBus("host=brws0002;virtualHost=/;username=admin;password=sstadev"
                //container => container
                    //.Register<ISaga<ExampleSagaInstance>, ExampleWorkflowSaga>()
                    //.Register<ExampleWorkflowSaga, ExampleWorkflowSaga>()
                    //.Register<ISagaRepository<ExampleSagaInstance>, ExampleSagaRepository>()
            //))

            RabbitHutch.SetContainerFactory(()=>new NinjectAdapter(kernel));

            using (var bus = RabbitHutch.CreateBus("host=brcentos2;virtualHost=/;username=admin;password=sstadev", (s)=> s.RegisterWorkflowComponents()))
            {   
                    
                bus.SubscribeWorkflow<ExampleWorkflow, ExampleMessage>("wf-ExampleWorkflow");

                
                bus.SubscribeConsumer<AdvancedExampleConsumer>("autoSubscribeAdvanced");

                //bus.SubscribeConsumer<ExampleConsumer>("autoSubscribe");
                //bus.SubscribeConsumer<ExampleResponder>("autoSubscribe");

                Console.WriteLine("Bus listening...");

                Console.WriteLine();
                Console.Write("Enter your name: ");
                string name = Console.ReadLine();

                bus.Publish(new ExampleMessage() { Name = name}, "ExampleWorkflow");

                /*
                while (true)
                {
                    var s = Console.ReadLine();
                    bus.Publish(new ChatMessage() { WorkflowId = WorkflowSagaHost.WorkflowId.ToString(), Message = s });
                    
                    if (!String.IsNullOrWhiteSpace(s) && s.Trim().ToLowerInvariant()=="goodbye")
                        break;
                }
                */
                Console.ReadLine();
            }
        }
    }    
}
