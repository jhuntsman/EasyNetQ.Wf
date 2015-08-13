using System;

namespace EasyNetQ.Contrib.Wf.Activities
{
    public interface IMessageActivity { }
    public interface IReceiveMessageActivity : IMessageActivity { }
    public interface IPublishMessageActivity : IMessageActivity { }

    /*
    public interface ISendMessageActivity : IMessageActivity { }
    public interface IRespondMessageActivity : IMessageActivity { }    
    */
}