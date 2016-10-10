using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence;
using Akka.EventStore.Cqrs.Core.Messages;
using Akka.EventStore.Cqrs.Core.Extensions;

namespace Akka.EventStore.Cqrs.Core
{
    public interface IAggregateRootCreationParameters
    {
        Guid Id { get; }
        IActorRef Projections { get; }
        int SnapshotThreshold { get; }

        TimeSpan? RecieveTimeout { get; }
    }

    public class AggregateRootCreationParameters : IAggregateRootCreationParameters
    {
        public AggregateRootCreationParameters(Guid id, IActorRef projections, int snapshotThreshold = 250, TimeSpan? receiveTimeout = null)
        {
            Id = id;
            Projections = projections;
            SnapshotThreshold = snapshotThreshold;
            RecieveTimeout = receiveTimeout;
        }
 

        public Guid Id { get; private set; }
        public IActorRef Projections { get; private set; }

        public TimeSpan? RecieveTimeout { get; private set; }

        /// <summary>
        /// Values less than 1 disable snapshotting
        /// </summary>
        public int SnapshotThreshold { get; private set; }
    }

    public abstract class AggregateRootActor<T> : 
        PersistentActor, 
        IEventSink
        where T : Domain.IPersistable
    {
        private readonly Guid _id;
        private readonly IActorRef _projections;
        private readonly int _snapshotThreshold;
        private readonly ICollection<Exception> _exceptions;
        private readonly ILoggingAdapter _log;
        private long LastSnapshottedVersion { get; set; }

        protected AggregateRootActor(IAggregateRootCreationParameters parameters)
        {
            _id = parameters.Id;
            _projections = parameters.Projections;
            
            // Disable snapshots on non snapshotable types
            if (typeof(ISnapShotable).IsAssignableFrom(typeof(T)))
                _snapshotThreshold = parameters.SnapshotThreshold; 
            else
                _snapshotThreshold = 0;

            _exceptions = new List<Exception>();
            _log = Context.GetLogger();

            if(parameters.RecieveTimeout.HasValue)
                Context.SetReceiveTimeout(parameters.RecieveTimeout.Value);

            
        }

        public override string PersistenceId
        {
            //get { return string.Format("{0}-agg-{1:n}", GetType().Name, _id).ToLowerInvariant(); }
            get { return _id.ToString(); }
        }

        void IEventSink.Publish(IEvent @event)
        {
            Persist(@event, e =>
            {
                Apply(e);

                // Generate snapshots
                if (ShouldSnapShot())
                {
                    this.Self.Tell(SaveAggregate.Message);
                }
                _projections.Tell(@event);
            });
        }

        protected override bool ReceiveRecover(object message)
        {
            if (message.CanHandle<RecoveryCompleted>(x =>
            {
                _log.Debug("Recovered state to version {0}", LastSequenceNr);
            }))
                return true;

            var snapshot = message.ReadMessage<SnapshotOffer>();
            if (snapshot.HasValue)
            {
                var offer = snapshot.Value;
                _log.Debug("State loaded from snapshot");
                LastSnapshottedVersion = offer.Metadata.SequenceNr;
                return RecoverState(offer.Snapshot, LastSnapshottedVersion);
            }

            if (message.CanHandle<IEvent>(@event =>
            {
                Apply(@event);
            }))
                return true;

            return false;
        }

        protected override bool ReceiveCommand(object message)
        {
            if (message.WasHandled<SaveAggregate>(x => Save()))
                return true;

            if (message.WasHandled<ICommand>(command =>
            {
                try
                {
                    var handled = Handle(command);
                    Sender.Tell(new CommandResponse(handled, _exceptions));
                    return handled;
                }
                catch (Exception e)
                {
                    Sender.Tell(e);
                    return false;
                }
            }))
                return true;

            if (message.WasHandled<ReceiveTimeout>(timeout =>
            {
                Context.Parent.Tell(new PassivateMessage(_id));
                return true;
            }))return true;

            return false;
        }

        private bool Save()
        {
            if (ShouldSnapShot())
            {
                var stateMomento = GetState();
                if (stateMomento != null)
                {
                    SaveSnapshot(stateMomento);
                    LastSnapshottedVersion = LastSequenceNr;
                }
            }

            return true;
        }

        private bool ShouldSnapShot()
        {
            return (_snapshotThreshold > 0 && (LastSequenceNr - LastSnapshottedVersion) >= _snapshotThreshold);
        }

        protected abstract bool Handle(ICommand command);
        protected abstract bool Apply(IEvent @event);
        protected abstract bool RecoverState(object state, long version);
        protected abstract object GetState();
    }
}