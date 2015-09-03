using System;

namespace EasyNetQ.Wf.Activities
{
    internal interface IMessageActivity { }
    internal interface IMessageReceiveActivity : IMessageActivity { }
    internal interface IPublishMessageActivity : IMessageActivity { }

    /*
    public interface ISendMessageActivity : IMessageActivity { }
    public interface IRespondMessageActivity : IMessageActivity { }    
    */
}