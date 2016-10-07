namespace Akka.EventStore.Cqrs.Core.Messages
{
    using System;
    public class PassivateMessage
    {
        public PassivateMessage(Guid id)
        {
            this.Id = id;
        }

        public Guid Id { get; private set; }
    }
}
